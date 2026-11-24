using System.Formats.Tar;
using System.IO.Compression;
using MLPlatform.ControlPlane.Application.Tracking;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Commit archives from GitHub, GitLab or Gitea: the request, and reading or writing a tar.gz.</summary>
public static class GitArchives
{
    public static HttpRequestMessage CommitRequest(string gitRemote, string reference, string? token)
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

    /// <summary>The commit sha in a commit-lookup answer (plain sha, or JSON with sha/id), or null.</summary>
    public static string? ParseCommit(string body)
    {
        body = body.Trim();
        var sha = body.StartsWith('[') || body.StartsWith('{')
            ? System.Text.RegularExpressions.Regex.Match(body, "\"(?:sha|id)\"\\s*:\\s*\"([0-9a-f]{40})\"").Groups[1].Value
            : body;
        return System.Text.RegularExpressions.Regex.IsMatch(sha, "^[0-9a-f]{40}$") ? sha : null;
    }

    public static HttpRequestMessage Request(string gitRemote, string commit, string? token)
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

    public static SortedDictionary<string, (byte[] Data, UnixFileMode Mode)> Read(byte[] archive, bool stripTopFolder)
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

    public static byte[] Write(SortedDictionary<string, (byte[] Data, UnixFileMode Mode)> files)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax))
            foreach (var (name, (data, mode)) in files)
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(data), ModificationTime = DateTimeOffset.UnixEpoch,
                    Mode = mode.HasFlag(UnixFileMode.UserExecute) ? (UnixFileMode)0b111_101_101 : (UnixFileMode)0b110_100_100,
                });
        return output.ToArray();
    }
}
