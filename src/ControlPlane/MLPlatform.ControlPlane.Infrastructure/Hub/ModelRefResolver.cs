using System.Net;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>hf:// references → commit + files; with the workspace token each file gets the Hub's signed link (or its
/// bytes), so the token never leaves the api.</summary>
public sealed class ModelRefResolver(HfHub hub, SecretBox box, IMediator mediator, IHttpClientFactory http) : IModelRefResolver
{
    private const long MaxInlineBytes = 20L << 20;

    public async Task<ModelFiles> ResolveAsync(Guid workspaceId, string reference, CancellationToken ct)
    {
        var (repo, revision, file) = Parse(reference);
        var token = box.Open(await mediator.Query(new GetHubSecretQuery(workspaceId), ct));
        var info = await hub.InfoAsync("model", repo, revision, token, ct);
        var sha = info["sha"]?.ToString() ?? throw new NotFoundException("HUB_REPO_NOT_FOUND", $"{repo}@{revision} has no commit");
        if (string.IsNullOrEmpty(token) && info["gated"] is { } gated && gated.ToString() is not ("false" or "False"))
            throw new ForbiddenException("HUB_ACCESS_DENIED", $"{repo} is gated: add a Hugging Face token in Settings › Integrations for an account that accepted its terms");
        var tree = await hub.TreeAsync("model", repo, sha, token, ct);
        var wanted = file is null ? tree : tree.Where(f => f.Path == file).ToList();
        if (wanted.Count == 0) throw new NotFoundException("HUB_FILE_NOT_FOUND", file is null ? $"{repo} has no files" : $"{repo} has no file {file}");
        var files = new List<ModelFile>();
        long inline = 0;
        foreach (var f in wanted)
        {
            var url = hub.ResolveUrl("model", repo, sha, f.Path);
            if (string.IsNullOrEmpty(token)) { files.Add(new ModelFile(f.Path, f.Size, url, null)); continue; }
            var shared = await SignedAsync(url, token, f.Size, inline, ct);
            inline += shared.Content?.Length ?? 0;
            files.Add(shared with { Path = f.Path });
        }
        return new ModelFiles(repo, sha, files);
    }

    private async Task<ModelFile> SignedAsync(string url, string token, long size, long inlineSoFar, CancellationToken ct)
    {
        using var head = new HttpRequestMessage(HttpMethod.Head, url);
        head.Headers.Authorization = new("Bearer", token);
        using var res = await http.CreateClient("hf-noredirect").SendAsync(head, ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ForbiddenException("HUB_ACCESS_DENIED", "Gated/private model: accept its terms on Hugging Face with the account of the workspace token");
        if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { IsAbsoluteUri: true } loc && !loc.Host.EndsWith("huggingface.co", StringComparison.Ordinal))
            return new ModelFile("", size, loc.ToString(), null);
        if (inlineSoFar + size > MaxInlineBytes)
            throw new ResourceExhaustedException("HUB_INLINE_TOO_LARGE", "Private model files without signed links exceed 20 MB");
        using var get = new HttpRequestMessage(HttpMethod.Get, url);
        get.Headers.Authorization = new("Bearer", token);
        using var body = await http.CreateClient("hf").SendAsync(get, ct);
        body.EnsureSuccessStatusCode();
        return new ModelFile("", size, "", await body.Content.ReadAsByteArrayAsync(ct));
    }

    public static (string Repo, string Revision, string? File) Parse(string reference)
    {
        var r = reference.StartsWith("hf://", StringComparison.OrdinalIgnoreCase) ? reference[5..] : reference;
        string? file = null;
        if (r.IndexOf("#file=", StringComparison.Ordinal) is var hash and >= 0) { file = r[(hash + 6)..]; r = r[..hash]; }
        var revision = "main";
        if (r.IndexOf('@') is var at and >= 0) { revision = r[(at + 1)..]; r = r[..at]; }
        if (r.Split('/') is not [{ Length: > 0 }, { Length: > 0 }])
            throw new ValidationException(new Dictionary<string, string> { ["ref"] = "Use hf://owner/name[@revision][#file=path]" });
        return (r, revision, file);
    }
}
