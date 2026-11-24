using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Fetches a commit's archive from GitHub, GitLab or Gitea (cached in S3), removes deleted files, lays the
/// developer's changed files over it and stores the result as a code snapshot.</summary>
public sealed class CodeSnapshotComposer(S3Client s3, SecretBox box, IHttpClientFactory http) : ICodeSnapshotComposer
{
    private const long MaxArchiveBytes = 200L << 20;

    public async Task<string> ComposeAsync(Guid workspaceId, CodeSourceRow source, string gitRemote, string commit, string? overlaySha256,
        IReadOnlyList<string> deleted, CancellationToken ct)
    {
        var baseKey = $"{workspaceId}/code/base/{Hash(CodeSourceUrls.Normalize(gitRemote))[..16]}-{commit}.tar.gz";
        var baseArchive = await s3.GetAsync(Buckets.Artifacts, baseKey, ct);
        if (baseArchive is null)
        {
            baseArchive = await DownloadAsync(source, gitRemote, commit, ct);
            await s3.PutAsync(Buckets.Artifacts, baseKey, baseArchive, "application/gzip", ct);
        }
        var files = GitArchives.Read(baseArchive, stripTopFolder: true);
        foreach (var d in deleted) files.Remove(d.Replace('\\', '/').Trim('/'));
        if (overlaySha256 is not null)
        {
            var overlay = await s3.GetAsync(Buckets.Artifacts, CodeRunRules.SnapshotKey(workspaceId, overlaySha256), ct)
                ?? throw new PreconditionException("TRACK_SNAPSHOT_MISSING", "The changed files were not uploaded");
            foreach (var (name, entry) in GitArchives.Read(overlay, stripTopFolder: false)) files[name] = entry;
        }
        var composed = GitArchives.Write(files);
        var sha = Hash(composed);
        var key = CodeRunRules.SnapshotKey(workspaceId, sha);
        if (await s3.HeadAsync(Buckets.Artifacts, key, ct) is null) await s3.PutAsync(Buckets.Artifacts, key, composed, "application/gzip", ct);
        return sha;
    }

    public async Task<string> ResolveCommitAsync(CodeSourceRow source, string gitRemote, string reference, CancellationToken ct)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(reference, "^[0-9a-f]{40}$")) return reference;
        using var request = GitArchives.CommitRequest(gitRemote, reference, box.Open(source.SecretEnc));
        using var response = await http.CreateClient("code-sources").SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new PreconditionException("TRACK_SOURCE_DENIED", $"{source.UrlPrefix} refused the token ({(int)response.StatusCode})");
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            throw new PreconditionException("TRACK_REF_NOT_FOUND", $"'{reference}' not found in {CodeSourceUrls.Normalize(gitRemote)}");
        response.EnsureSuccessStatusCode();
        return GitArchives.ParseCommit(await response.Content.ReadAsStringAsync(ct))
               ?? throw new PreconditionException("TRACK_REF_NOT_FOUND", $"'{reference}' did not resolve to a commit");
    }

    public async Task<string?> ReadFileAsync(Guid workspaceId, string snapshotSha256, string path, CancellationToken ct) =>
        await s3.GetAsync(Buckets.Artifacts, CodeRunRules.SnapshotKey(workspaceId, snapshotSha256), ct) is { } archive
        && GitArchives.Read(archive, stripTopFolder: false).TryGetValue(path, out var file) ? Encoding.UTF8.GetString(file.Data) : null;

    private async Task<byte[]> DownloadAsync(CodeSourceRow source, string gitRemote, string commit, CancellationToken ct)
    {
        using var request = GitArchives.Request(gitRemote, commit, box.Open(source.SecretEnc));
        using var response = await http.CreateClient("code-sources").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new PreconditionException("TRACK_SOURCE_DENIED", $"{source.UrlPrefix} refused the token ({(int)response.StatusCode})");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new PreconditionException("TRACK_COMMIT_NOT_FOUND", $"Commit {commit[..12]} not found in {CodeSourceUrls.Normalize(gitRemote)}: push it, or check the token");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxArchiveBytes) throw new PreconditionException("TRACK_SOURCE_TOO_LARGE", "Repository archive is larger than 200 MB");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (copy.Length + read > MaxArchiveBytes) throw new PreconditionException("TRACK_SOURCE_TOO_LARGE", "Repository archive is larger than 200 MB");
            copy.Write(buffer, 0, read);
        }
        return copy.ToArray();
    }

    private static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
