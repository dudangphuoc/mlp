using MLPlatform.ControlPlane.Application.Common;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>
/// Hugging Face Hub client (public REST): search models/datasets, repo info, file tree, file download. The workspace token (if any)
/// is sent to read gated/private repos. No other user information is sent to HF.
/// </summary>
public sealed class HfHub(IHttpClientFactory http, IConfiguration cfg, TimeProvider time)
{
    // Short cache for search/info/tree pages: browsing back and forth does not hit the Hub again (key includes the token hash).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset At, object Value)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private async Task<T> CachedAsync<T>(string key, string? token, Func<Task<T>> load) where T : class
    {
        var k = key + "|" + (token is null ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..12]);
        var now = time.GetUtcNow();
        if (_cache.TryGetValue(k, out var hit) && now - hit.At < CacheTtl && hit.Value is T v) return v;
        var value = await load();
        if (_cache.Count > 2000) _cache.Clear();
        _cache[k] = (now, value);
        return value;
    }

    private string Base => (cfg["Hub:Endpoint"] ?? "https://huggingface.co").TrimEnd('/');
    private static string Seg(string kind) => kind == "dataset" ? "datasets" : "models";

    private HttpRequestMessage Req(HttpMethod m, string url, string? token)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.UserAgent.ParseAdd("mlplatform-hub/1.0");
        if (!string.IsNullOrEmpty(token)) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return r;
    }

    private async Task<JsonNode?> GetJsonAsync(string url, string? token, CancellationToken ct)
    {
        using var res = await http.CreateClient("hf").SendAsync(Req(HttpMethod.Get, url, token), ct);
        if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new ForbiddenException("HUB_ACCESS_DENIED", "Gated/private repo: add a valid Hugging Face token in Settings › Integrations (and accept the terms on the repo page)");
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) throw new NotFoundException("HUB_REPO_NOT_FOUND", "Repo / revision not found on Hugging Face");
        if (!res.IsSuccessStatusCode) throw new UnavailableException("HUB_UNAVAILABLE", $"Hugging Face returned {(int)res.StatusCode}");
        return await JsonNode.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    public async Task<string?> WhoAmIAsync(string token, CancellationToken ct)
    {
        using var res = await http.CreateClient("hf").SendAsync(Req(HttpMethod.Get, $"{Base}/api/whoami-v2", token), ct);
        if (!res.IsSuccessStatusCode) return null;
        return (await JsonNode.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct))?["name"]?.ToString();
    }

    /// <summary>One page of search results + the cursor of the next page (the Hub paginates with a Link: rel="next" header).</summary>
    public Task<HubSearchPage> SearchAsync(string kind, string query, string task, string sort, int limit, string? cursor, string? token, CancellationToken ct)
    {
        string url;
        if (!string.IsNullOrEmpty(cursor))
        {
            // The cursor is the Hub's own next-page URL; only accept URLs on the configured Hub host.
            if (!Uri.TryCreate(cursor, UriKind.Absolute, out var cu) || !cursor.StartsWith(Base + "/api/", StringComparison.Ordinal) || cu.Query.Length == 0)
                throw new InvalidArgumentException("HUB_CURSOR_INVALID", "Invalid search cursor");
            url = cursor;
        }
        else
        {
            var q = new StringBuilder($"{Base}/api/{Seg(kind)}?limit={Math.Clamp(limit, 1, 100)}&full=false&cardData=true&sort={Uri.EscapeDataString(string.IsNullOrEmpty(sort) ? "downloads" : sort)}&direction=-1");
            if (!string.IsNullOrWhiteSpace(query)) q.Append("&search=").Append(Uri.EscapeDataString(query.Trim()));
            if (!string.IsNullOrWhiteSpace(task)) q.Append(kind == "dataset" ? "&filter=task_categories:" : "&pipeline_tag=").Append(Uri.EscapeDataString(task.Trim()));
            url = q.ToString();
        }
        return CachedAsync("search:" + url, token, async () =>
        {
            using var res = await http.CreateClient("hf").SendAsync(Req(HttpMethod.Get, url, token), ct);
            if (!res.IsSuccessStatusCode) throw new UnavailableException("HUB_UNAVAILABLE", $"Hugging Face returned {(int)res.StatusCode}");
            var items = await JsonNode.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct) as JsonArray ?? [];
            return new HubSearchPage(items, NextLink(res));
        });
    }

    private static string? NextLink(HttpResponseMessage res) =>
        res.Headers.TryGetValues("Link", out var links) && Regex.Match(string.Join(",", links), "<([^>]+)>;\\s*rel=\"next\"") is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>Entries of one folder (non-recursive, all pages). Folders come back with Dir = true.</summary>
    public Task<List<HubFileInfo>> ListFolderAsync(string kind, string repo, string revision, string path, string? token, CancellationToken ct) =>
        CachedAsync($"tree:{kind}:{repo}@{revision}/{path}", token, async () =>
        {
            var files = new List<HubFileInfo>();
            var p = string.IsNullOrEmpty(path) ? "" : "/" + string.Join('/', path.Trim('/').Split('/').Select(Uri.EscapeDataString));
            string? url = $"{Base}/api/{Seg(kind)}/{repo}/tree/{Uri.EscapeDataString(revision)}{p}";
            for (var page = 0; url is not null && page < 20; page++)
            {
                using var res = await http.CreateClient("hf").SendAsync(Req(HttpMethod.Get, url, token), ct);
                if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    throw new ForbiddenException("HUB_ACCESS_DENIED", "Repo is gated/private — a valid Hugging Face token is needed");
                if (!res.IsSuccessStatusCode) throw new NotFoundException("HUB_REPO_NOT_FOUND", "Could not read the repo's file list");
                if (await JsonNode.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct) is JsonArray arr)
                    foreach (var e in arr)
                        if (e?["type"]?.ToString() is "file" or "directory" && e["path"]?.ToString() is { } fp)
                            files.Add(e["type"]!.ToString() == "directory" ? new HubFileInfo(fp, 0, false, true)
                                : new HubFileInfo(fp, e["lfs"]?["size"]?.GetValue<long>() ?? e["size"]?.GetValue<long>() ?? 0, e["lfs"] is not null));
                url = NextLink(res);
            }
            return files;
        });

    /// <summary>Public file URL pinned to a commit (used by reference imports — machines download straight from the Hub).</summary>
    public string ResolveUrl(string kind, string repo, string revision, string path) =>
        $"{Base}/{(kind == "dataset" ? "datasets/" : "")}{repo}/resolve/{Uri.EscapeDataString(revision)}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";

    public Task<JsonNode> InfoAsync(string kind, string repo, string revision, string? token, CancellationToken ct) =>
        CachedAsync($"info:{kind}:{repo}@{revision}", token, async () =>
            await GetJsonAsync($"{Base}/api/{Seg(kind)}/{repo}/revision/{Uri.EscapeDataString(revision)}", token, ct) ?? new JsonObject());

    /// <summary>Recursive file tree (paged via the Link header).</summary>
    public async Task<List<HubFileInfo>> TreeAsync(string kind, string repo, string revision, string? token, CancellationToken ct)
    {
        var files = new List<HubFileInfo>();
        string? url = $"{Base}/api/{Seg(kind)}/{repo}/tree/{Uri.EscapeDataString(revision)}?recursive=true&expand=false";
        for (var page = 0; url is not null && page < 50; page++)
        {
            using var res = await http.CreateClient("hf").SendAsync(Req(HttpMethod.Get, url, token), ct);
            if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new ForbiddenException("HUB_ACCESS_DENIED", "Gated/private repo: a valid Hugging Face token is required");
            if (!res.IsSuccessStatusCode) throw new NotFoundException("HUB_REPO_NOT_FOUND", "Could not read the repo's file list");
            if (await JsonNode.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct) is JsonArray arr)
                foreach (var e in arr)
                    if (e?["type"]?.ToString() == "file")
                        files.Add(new HubFileInfo(e["path"]!.ToString(), e["lfs"]?["size"]?.GetValue<long>() ?? e["size"]?.GetValue<long>() ?? 0, e["lfs"] is not null));
            url = res.Headers.TryGetValues("Link", out var links)
                ? Regex.Match(string.Join(",", links), "<([^>]+)>;\\s*rel=\"next\"") is { Success: true } m ? m.Groups[1].Value : null : null;
        }
        return files;
    }

    /// <summary>GET an already-resolved Hub file URL (with the token for gated repos).</summary>
    public async Task<HttpResponseMessage> DownloadUrlAsync(string url, string? token, CancellationToken ct)
    {
        if (!url.StartsWith(Base + "/", StringComparison.Ordinal)) throw new InvalidArgumentException("HUB_URL_INVALID", "Not a Hugging Face URL");
        return await http.CreateClient("hf").SendAsync(Req(HttpMethod.Get, url, token), HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public async Task<HttpResponseMessage> DownloadAsync(string kind, string repo, string revision, string path, string? token, CancellationToken ct)
    {
        var prefix = kind == "dataset" ? "datasets/" : "";
        var url = $"{Base}/{prefix}{repo}/resolve/{Uri.EscapeDataString(revision)}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";
        var res = await http.CreateClient("hf").SendAsync(Req(HttpMethod.Get, url, token), HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        return res;
    }

    public Task<string> CardExcerptAsync(string kind, string repo, string revision, string? token, CancellationToken ct) =>
        CachedAsync($"card:{kind}:{repo}@{revision}", token, () => LoadCardAsync(kind, repo, revision, token, ct));

    private async Task<string> LoadCardAsync(string kind, string repo, string revision, string? token, CancellationToken ct)
    {
        try
        {
            using var res = await DownloadAsync(kind, repo, revision, "README.md", token, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (text.StartsWith("---", StringComparison.Ordinal) && text.IndexOf("\n---", 3, StringComparison.Ordinal) is var end and > 0) text = text[(end + 4)..];
            return text.Trim().Length > 1500 ? text.Trim()[..1500] + "…" : text.Trim();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return ""; }
    }

    /// <summary>Simple glob: <c>*</c> within one segment, <c>**</c> across segments.</summary>
    public static bool Match(string path, IReadOnlyList<string> include)
    {
        if (include.Count == 0) return !path.EndsWith(".gitattributes", StringComparison.Ordinal);
        foreach (var g in include)
        {
            var re = "^" + Regex.Escape(g).Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$";
            if (Regex.IsMatch(path, re) || (!g.Contains('/') && Regex.IsMatch(Path.GetFileName(path), re))) return true;
        }
        return false;
    }
}
