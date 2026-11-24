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
        var files = Read(baseArchive, stripTopFolder: true);
        foreach (var d in deleted) files.Remove(d.Replace('\\', '/').Trim('/'));
        if (overlaySha256 is not null)
        {
            var overlay = await s3.GetAsync(Buckets.Artifacts, CodeRunRules.SnapshotKey(workspaceId, overlaySha256), ct)
                ?? throw new PreconditionException("TRACK_SNAPSHOT_MISSING", "The changed files were not uploaded");
            foreach (var (name, entry) in Read(overlay, stripTopFolder: false)) files[name] = entry;
        }
        var composed = Write(files);
        var sha = Hash(composed);
        var key = CodeRunRules.SnapshotKey(workspaceId, sha);
        if (await s3.HeadAsync(Buckets.Artifacts, key, ct) is null) await s3.PutAsync(Buckets.Artifacts, key, composed, "application/gzip", ct);
        return sha;
    }

    public async Task<string> ResolveCommitAsync(CodeSourceRow source, string gitRemote, string reference, CancellationToken ct)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(reference, "^[0-9a-f]{40}$")) return reference;
        using var request = CommitRequest(gitRemote, reference, box.Open(source.SecretEnc));
        using var response = await http.CreateClient("code-sources").SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new PreconditionException("TRACK_SOURCE_DENIED", $"{source.UrlPrefix} refused the token ({(int)response.StatusCode})");
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            throw new PreconditionException("TRACK_REF_NOT_FOUND", $"'{reference}' not found in {CodeSourceUrls.Normalize(gitRemote)}");
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
        var sha = body.StartsWith('[') || body.StartsWith('{')
            ? System.Text.RegularExpressions.Regex.Match(body, "\"(?:sha|id)\"\\s*:\\s*\"([0-9a-f]{40})\"").Groups[1].Value
            : body;
        return System.Text.RegularExpressions.Regex.IsMatch(sha, "^[0-9a-f]{40}$") ? sha
            : throw new PreconditionException("TRACK_REF_NOT_FOUND", $"'{reference}' did not resolve to a commit");
    }

    public async Task<string?> ReadFileAsync(Guid workspaceId, string snapshotSha256, string path, CancellationToken ct) =>
        await s3.GetAsync(Buckets.Artifacts, CodeRunRules.SnapshotKey(workspaceId, snapshotSha256), ct) is { } archive
        && Read(archive, stripTopFolder: false).TryGetValue(path, out var file) ? Encoding.UTF8.GetString(file.Data) : null;

    private static HttpRequestMessage CommitRequest(string gitRemote, string reference, string? token)
    {
        var host = new Uri(CodeSourceUrls.Normalize(gitRemote)).Host;
        var (owner, repo) = CodeSourceUrls.OwnerRepo(gitRemote);
        var r = Uri.EscapeDataString(reference);
        HttpRequestMessage request;
        if (host == "github.com")
        {
            request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/commits/{r}");
            request.Headers.Accept.ParseAdd("application/vnd.github.sha");
            if (token is { Length: > 0 }) request.Headers.Authorization = new("Bearer", token);
        }
        else if (host.Contains("gitlab", StringComparison.Ordinal))
        {
            request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/api/v4/projects/{Uri.EscapeDataString(owner + "/" + repo)}/repository/commits/{r}");
            if (token is { Length: > 0 }) request.Headers.Add("PRIVATE-TOKEN", token);
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/api/v1/repos/{owner}/{repo}/commits?sha={r}&limit=1");
            if (token is { Length: > 0 }) request.Headers.Authorization = new("token", token);
        }
        request.Headers.UserAgent.ParseAdd("mlplatform");
        return request;
    }

    private async Task<byte[]> DownloadAsync(CodeSourceRow source, string gitRemote, string commit, CancellationToken ct)
    {
        using var request = ArchiveRequest(gitRemote, commit, box.Open(source.SecretEnc));
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

    private static HttpRequestMessage ArchiveRequest(string gitRemote, string commit, string? token)
    {
        var host = new Uri(CodeSourceUrls.Normalize(gitRemote)).Host;
        var (owner, repo) = CodeSourceUrls.OwnerRepo(gitRemote);
        HttpRequestMessage request;
        if (host == "github.com")
        {
            request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/tarball/{commit}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (token is { Length: > 0 }) request.Headers.Authorization = new("Bearer", token);
        }
        else if (host.Contains("gitlab", StringComparison.Ordinal))
        {
            request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/api/v4/projects/{Uri.EscapeDataString(owner + "/" + repo)}/repository/archive.tar.gz?sha={commit}");
            if (token is { Length: > 0 }) request.Headers.Add("PRIVATE-TOKEN", token);
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/api/v1/repos/{owner}/{repo}/archive/{commit}.tar.gz");
            if (token is { Length: > 0 }) request.Headers.Authorization = new("token", token);
        }
        request.Headers.UserAgent.ParseAdd("mlplatform");
        return request;
    }

    private static SortedDictionary<string, (byte[] Data, UnixFileMode Mode)> Read(byte[] archive, bool stripTopFolder)
    {
        var files = new SortedDictionary<string, (byte[], UnixFileMode)>(StringComparer.Ordinal);
        using var gz = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress);
        using var tar = new TarReader(gz);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null) continue;
            var name = entry.Name.Replace('\\', '/').TrimStart('.', '/');
            if (stripTopFolder) name = name.Contains('/') ? name[(name.IndexOf('/') + 1)..] : "";
            if (name.Length == 0 || name.Split('/').Contains("..")) continue;
            using var data = new MemoryStream();
            entry.DataStream.CopyTo(data);
            files[name] = (data.ToArray(), entry.Mode);
        }
        return files;
    }

    private static byte[] Write(SortedDictionary<string, (byte[] Data, UnixFileMode Mode)> files)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Ustar))
            foreach (var (name, (data, mode)) in files)
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(data), ModificationTime = DateTimeOffset.UnixEpoch,
                    Mode = mode.HasFlag(UnixFileMode.UserExecute) ? (UnixFileMode)0b111_101_101 : (UnixFileMode)0b110_100_100,
                });
        return output.ToArray();
    }

    private static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
