using System.Net;
using System.Security.Cryptography;
using System.Text;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Source archives of recipes: the upstream repository at the pinned commit, top folder removed, kept in S3
/// and handed to the agent as a signed link (the build itself never clones).</summary>
public sealed class RecipeSourceStore(S3Client s3, IHttpClientFactory http, IConfiguration cfg)
{
    private const long MaxArchiveBytes = 400L << 20;

    /// <summary>Replaces each git: placeholder of the build with a signed link and the archive's sha256.</summary>
    public async Task MaterializeAsync(ImageBuild build, CancellationToken ct)
    {
        foreach (var f in build.Fetches.Where(f => f.Url.StartsWith(MLPlatform.Recipes.RecipeDockerfile.GitScheme, StringComparison.Ordinal)))
        {
            var spec = f.Url[MLPlatform.Recipes.RecipeDockerfile.GitScheme.Length..];
            var at = spec.LastIndexOf('@');
            var (key, sha) = await ArchiveAsync(spec[..at], spec[(at + 1)..], ct);
            f.Url = s3.Presign(HttpMethod.Get, Buckets.Artifacts, key, TimeSpan.FromHours(2), Audience.Workload);
            f.Sha256 = sha;
        }
    }

    private async Task<(string Key, string Sha256)> ArchiveAsync(string git, string commit, CancellationToken ct)
    {
        var (owner, repo) = MLPlatform.ControlPlane.Application.Tracking.CodeSourceUrls.OwnerRepo(git);
        var host = new Uri(MLPlatform.ControlPlane.Application.Tracking.CodeSourceUrls.Normalize(git)).Host;
        var key = $"catalog/src/{host}/{owner}/{repo}/{commit}.tar.gz";
        if (await s3.GetAsync(Buckets.Artifacts, key + ".sha256", ct) is { } known) return (key, Encoding.UTF8.GetString(known).Trim());
        var repacked = GitArchives.Write(GitArchives.Read(await DownloadAsync(git, commit, ct), stripTopFolder: true));
        var sha = Convert.ToHexStringLower(SHA256.HashData(repacked));
        await s3.PutAsync(Buckets.Artifacts, key, repacked, "application/gzip", ct);
        await s3.PutAsync(Buckets.Artifacts, key + ".sha256", Encoding.UTF8.GetBytes(sha), "text/plain", ct);
        return (key, sha);
    }

    private async Task<byte[]> DownloadAsync(string git, string commit, CancellationToken ct)
    {
        using var request = GitArchives.Request(git, commit, cfg["Catalog:GitHubToken"]);
        using var response = await http.CreateClient("code-sources").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new PreconditionException("CATALOG_SOURCE_NOT_FOUND", $"Commit {commit[..Math.Min(12, commit.Length)]} not found in {git}");
        if (!response.IsSuccessStatusCode)
            throw new UnavailableException("CATALOG_SOURCE_UNAVAILABLE", $"{git} answered {(int)response.StatusCode} for the source archive");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (copy.Length + read > MaxArchiveBytes) throw new PreconditionException("CATALOG_SOURCE_TOO_LARGE", $"{git} archive is larger than 400 MB");
            copy.Write(buffer, 0, read);
        }
        return copy.ToArray();
    }
}
