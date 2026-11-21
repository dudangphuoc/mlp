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

namespace MLPlatform.Api.Infrastructure;

/// <summary>Encryption of integration secrets (AES-GCM, key derived from the instance pepper); the DB stores only ciphertext (M01 R10).</summary>
public sealed class SecretBox(IConfiguration cfg)
{
    private byte[] Key => HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg["Secrets:TokenPepper"] ?? "dev"), "mlp-integrations"u8);

    public string Seal(string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plain);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Key, 16)) aes.Encrypt(nonce, pt, ct, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. ct]);
    }

    public string? Open(string? sealedText)
    {
        if (string.IsNullOrEmpty(sealedText)) return null;
        try
        {
            var b = Convert.FromBase64String(sealedText);
            var pt = new byte[b.Length - 28];
            using var aes = new AesGcm(Key, 16);
            aes.Decrypt(b.AsSpan(0, 12), b.AsSpan(28), b.AsSpan(12, 16), pt);
            return Encoding.UTF8.GetString(pt);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException) { return null; }
    }
}

/// <summary>A file in an HF repo.</summary>
public sealed record HubFileInfo(string Path, long Size, bool Lfs, bool Dir = false);

/// <summary>A page of Hub search results; Next is the cursor of the following page.</summary>
public sealed record HubSearchPage(JsonArray Items, string? Next);

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

/// <summary>
/// HF import worker (one job at a time: bandwidth + CPU): downloads each file from HF and PUTs it straight to S3 storage via an internal signed URL (streamed, no
/// temp storage). Dataset: writes files into the dataset, then commits version "Imported from HF repo@rev". Model: D-28 model folder (<c>weights/…</c> +
/// <c>recipe.lock.json</c>), a version with source hf_hub using the hf-auto recipe. Size limit: <c>Hub:MaxImportBytes</c> (default 50 GiB).
/// </summary>
public sealed class HubImporter(IServiceScopeFactory scopes, HfHub hub, SecretBox secrets, S3Client s3, IHttpClientFactory http, IConfiguration cfg,
    SystemHealth health, ILogger<HubImporter> log) : BackgroundService, IHubImportQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    public void Enqueue(Guid id) => _queue.Writer.TryWrite(id);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try   // a cleanup failure must not stop the whole api (BackgroundService exceptions stop the host)
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new FailStaleHubImportsCommand(), stop) is > 0 and var n)
                log.LogWarning("{N} unfinished HF import(s) marked failed (api restarted)", n);
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Could not mark unfinished imports as failed"); }
        health.Beat("hub-import", 0);
        await foreach (var id in _queue.Reader.ReadAllAsync(stop))
        {
            health.Beat("hub-import", 0);
            try { await RunAsync(id, stop); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "HF import {Id} failed", id);
                await UpdateAsync(id, "failed", null, 0, 0, 0, 0, e is AppException ae ? ae.Message : e.Message);
            }
        }
    }

    private async Task UpdateAsync(Guid id, string state, Guid? target, int ft, int fd, long bt, long bd, string msg)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new UpdateHubImportCommand(id, state, target, ft, fd, bt, bd, msg));
    }

    /// <summary>One file to import: relative path, size, and where to read it from.</summary>
    private sealed record SourceFile(string Path, long Size, string Url);

    private async Task RunAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        // Enqueued from inside a transaction (pipeline engine) → the row may not be committed yet: wait briefly before giving up.
        var job = await m.Query(new GetHubImportJobQuery(id), ct);
        for (var i = 0; job is null && i < 20; i++)
        {
            await Task.Delay(500, ct);
            job = await m.Query(new GetHubImportJobQuery(id), ct);
        }
        if (job is not { State: "queued" }) return;
        scope.ServiceProvider.GetRequiredService<CurrentUser>().ActAs(job.CreatedBy);
        var token = job.Source == "hf" ? secrets.Open(await m.Query(new GetHubSecretQuery(job.WorkspaceId), ct)) : null;
        await UpdateAsync(id, "running", job.TargetId, 0, 0, 0, 0, "Listing files");

        // 1. Files from the source (HF revision pinned to its commit so a reference stays immutable).
        string sourceRef;
        List<SourceFile> files;
        var gated = false;
        switch (job.Source)
        {
            case "url":
                files = await ListUrlsAsync(job.Include, ct);
                sourceRef = job.Include.Length == 1 ? job.Include[0] : $"{job.Include.Length} URLs";
                break;
            case "s3":
                var (bucket0, prefix0) = ParseS3(job.Repo);
                files = (await s3.ListAsync(bucket0, prefix0, ct)).Where(o => !o.Key.EndsWith('/'))
                    .Select(o => new SourceFile(o.Key[prefix0.Length..].TrimStart('/'), o.Size, $"s3://{bucket0}/{o.Key}"))
                    .Where(f => f.Path.Length > 0 && HfHub.Match(f.Path, job.Include)).ToList();
                sourceRef = job.Repo;
                break;
            default:
                var info = await hub.InfoAsync(job.Kind, job.Repo, job.Revision, token, ct);
                var sha = info["sha"]?.ToString() is { Length: > 0 } s ? s : job.Revision;
                gated = info["gated"] is JsonValue gv && (gv.TryGetValue<bool>(out var gb) ? gb : gv.ToString() is "auto" or "manual") || info["private"]?.GetValue<bool>() == true;
                files = (await hub.TreeAsync(job.Kind, job.Repo, sha, token, ct)).Where(f => HfHub.Match(f.Path, job.Include))
                    .Select(f => new SourceFile(f.Path, f.Size, hub.ResolveUrl(job.Kind, job.Repo, sha, f.Path))).ToList();
                sourceRef = $"hf://{(job.Kind == "dataset" ? "datasets/" : "")}{job.Repo}@{sha}";
                break;
        }
        if (files.Count == 0) { await UpdateAsync(id, "failed", null, 0, 0, 0, 0, "No files match the filter"); return; }
        var total = files.Sum(f => f.Size);

        // 2a. Reference: record the addresses, no copy. Gated/private HF repos need the workspace token, which machines don't have.
        if (job.Mode == "reference")
        {
            if (gated) { await UpdateAsync(id, "failed", null, files.Count, 0, total, 0, "This repo is gated/private — import it with \"Copy to storage\" instead"); return; }
            var refs = files.Select(f => (f.Path, f.Size, f.Url)).ToList();
            Guid target;
            if (job.Kind == "model")
            {
                var (versionId, _) = await m.Send(new ReserveHubModelVersionCommand(job.WorkspaceId, job.TargetName, job.Task, sourceRef, SourceKind(job.Source)), ct);
                await m.Send(new SetModelVersionReferenceCommand(versionId, refs), ct);
                target = versionId;
            }
            else
            {
                await m.Send(new CreateReferenceVersionCommand(job.WorkspaceId, job.TargetId!.Value, "Reference to " + sourceRef, sourceRef, refs), ct);
                target = job.TargetId!.Value;
            }
            await UpdateAsync(id, "succeeded", target, files.Count, files.Count, total, 0,
                job.Kind == "model" ? "Model version created (reference — downloaded when used)" : "Dataset version created (reference — downloaded when used)");
            log.LogInformation("Reference import {Source} done: {N} file(s)", sourceRef, files.Count);
            return;
        }

        // 2b. Copy into our storage.
        var max = long.TryParse(cfg["Hub:MaxImportBytes"], out var mx) ? mx : 50L * 1024 * 1024 * 1024;
        if (total > max) { await UpdateAsync(id, "failed", null, files.Count, 0, total, 0, $"The source is {total / 1073741824.0:0.0} GiB, over the {max / 1073741824} GiB limit; filter out some files or use a reference import"); return; }
        string bucket, prefix;
        Guid? targetId = job.TargetId;
        if (job.Kind == "model")
        {
            var (versionId, p) = await m.Send(new ReserveHubModelVersionCommand(job.WorkspaceId, job.TargetName, job.Task, sourceRef, SourceKind(job.Source)), ct);
            (bucket, prefix, targetId) = (Buckets.Artifacts, p + "weights/", versionId);
        }
        else (bucket, prefix) = (Buckets.Datasets, Buckets.Raw(job.WorkspaceId, job.TargetId!.Value));
        await UpdateAsync(id, "running", targetId, files.Count, 0, total, 0, "Copying");

        long done = 0;
        var n = 0;
        var put = http.CreateClient("s3-put");
        foreach (var f in files)
        {
            var key = prefix + Buckets.SafePath(f.Path);
            if (f.Url.StartsWith("s3://", StringComparison.Ordinal))
            {
                var (sb, sk) = ParseS3(f.Url);
                await s3.CopyAsync(sb, sk, bucket, key, ct);
            }
            else
            {
                using var src = job.Source == "hf"
                    ? await hub.DownloadUrlAsync(f.Url, token, ct)
                    : await http.CreateClient("import").GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                src.EnsureSuccessStatusCode();
                var url = s3.Presign(HttpMethod.Put, bucket, key, TimeSpan.FromHours(6), Audience.Internal);
                using var body = new StreamContent(await src.Content.ReadAsStreamAsync(ct));
                body.Headers.ContentLength = src.Content.Headers.ContentLength ?? f.Size;
                using var res = await put.PutAsync(url, body, ct);
                if (!res.IsSuccessStatusCode) throw new IOException($"S3 storage returned {(int)res.StatusCode} while writing {f.Path}");
            }
            if (job.Kind == "dataset") await m.Send(new CompleteUploadCommand(job.WorkspaceId, job.TargetId!.Value, f.Path), ct);
            done += f.Size; n++;
            await UpdateAsync(id, "running", targetId, files.Count, n, total, done, f.Path);
        }
        if (job.Kind == "model")
        {
            var root = prefix[..^"weights/".Length];
            var lockJson = new JsonObject
            {
                ["recipe"] = "hf-auto", ["variant"] = "hub-model", ["source"] = SourceKind(job.Source), ["ref"] = sourceRef,
                ["imported_at"] = TimeProvider.System.GetUtcNow().ToString("o", CultureInfo.InvariantCulture), ["files"] = files.Count,
            };
            await s3.PutAsync(Buckets.Artifacts, root + "recipe.lock.json", Encoding.UTF8.GetBytes(lockJson.ToJsonString()), "application/json", ct);
            await m.Send(new FinishHubModelVersionCommand(targetId!.Value, files.Count + 1, total), ct);
        }
        else await m.Send(new CreateVersionCommand(job.WorkspaceId, job.TargetId!.Value, "Imported from " + sourceRef), ct);
        await UpdateAsync(id, "succeeded", targetId, files.Count, n, total, done, job.Kind == "model" ? "Model version created" : "Dataset version committed");
        log.LogInformation("Import {Source} done: {N} file(s), {Bytes} bytes", sourceRef, n, done);
    }

    private static string SourceKind(string source) => source switch { "url" => "url", "s3" => "s3", _ => "hf_hub" };

    private static (string Bucket, string Key) ParseS3(string uri)
    {
        var rest = uri["s3://".Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? (rest, "") : (rest[..slash], rest[(slash + 1)..]);
    }

    /// <summary>File names + sizes of URL sources (HEAD; size 0 when the server does not say).</summary>
    private async Task<List<SourceFile>> ListUrlsAsync(string[] urls, CancellationToken ct)
    {
        var client = http.CreateClient("import");
        var used = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<SourceFile>();
        foreach (var u in urls)
        {
            long size = 0;
            try
            {
                using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, u), ct);
                if (!head.IsSuccessStatusCode) throw new NotFoundException("IMPORT_URL_UNREACHABLE", $"{u} returned {(int)head.StatusCode}");
                size = head.Content.Headers.ContentLength ?? 0;
            }
            catch (HttpRequestException e) { throw new NotFoundException("IMPORT_URL_UNREACHABLE", $"Cannot reach {u}: {e.Message}"); }
            var name = Uri.UnescapeDataString(new Uri(u).AbsolutePath.TrimEnd('/').Split('/')[^1]);
            if (name.Length == 0) name = "file";
            var unique = name;
            for (var i = 2; !used.Add(unique); i++) unique = $"{System.IO.Path.GetFileNameWithoutExtension(name)}-{i}{System.IO.Path.GetExtension(name)}";
            list.Add(new SourceFile(unique, size, u));
        }
        return list;
    }
}
