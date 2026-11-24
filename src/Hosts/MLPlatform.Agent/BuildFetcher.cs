using System.Security.Cryptography;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent;

/// <summary>Downloads the large files of a build context (source archives) and checks their sha256.</summary>
internal static class BuildFetcher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };

    /// <summary>Null when every file arrived intact, otherwise the reason.</summary>
    public static async Task<string?> FetchAsync(string dir, RepeatedField<BuildFetch> fetches, ILogger log, CancellationToken ct)
    {
        foreach (var f in fetches)
        {
            if (HostFiles.Resolve(dir, f.Path) is not { } path) return $"{f.Path}: path outside the build directory";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                using var response = await Http.GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) return $"{f.Path}: download failed ({(int)response.StatusCode})";
                await using (var file = File.Create(path))
                await using (var body = await response.Content.ReadAsStreamAsync(ct))
                    await body.CopyToAsync(file, ct);
                await using var check = File.OpenRead(path);
                var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct));
                if (f.Sha256.Length > 0 && !string.Equals(sha, f.Sha256, StringComparison.OrdinalIgnoreCase))
                    return $"{f.Path}: sha256 {sha} does not match {f.Sha256}";
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
                return $"{f.Path}: download failed ({e.Message})";
            }
            log.LogInformation("build fetch {Path}: {Bytes} bytes in {Sec:0.0}s", f.Path, new FileInfo(path).Length,
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
        return null;
    }
}
