using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace MLPlatform.Shim;

/// <summary>Files the recipe needs next to its code (pretrained backbones): fetched when the job starts, not baked
/// into the image, and kept by sha256 in the workspace's model cache on the machine.</summary>
public sealed partial class Runner
{
    private const string ModelCache = "/mnt/mlp-models";

    private async Task DownloadSourceFilesAsync(CancellationToken ct)
    {
        var items = (spec.Manifest["source"]?["downloads"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            var url = items[i]["url"]?.ToString() ?? "";
            var sha = items[i]["sha256"]?.ToString() ?? "";
            var path = items[i]["path"]?.ToString() ?? "";
            if (url.Length == 0 || path.Length == 0 || File.Exists(path)) continue;
            if (_ops is not null) await _ops.ProgressAsync("preparing_data", i, items.Count, "", $"downloading {Path.GetFileName(path)}");
            var blob = await CachedBlobAsync(url, sha, ct);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.CreateSymbolicLink(path, blob);
            log.WriteLine($"[mlp-shim] {url} → {path}");
        }
    }

    private static async Task<string> CachedBlobAsync(string url, string sha, CancellationToken ct)
    {
        var dir = Path.Combine(Directory.Exists(ModelCache) ? ModelCache : "/tmp/mlp-models", "blobs");
        Directory.CreateDirectory(dir);
        var blob = Path.Combine(dir, sha.Length == 64 ? sha : Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))));
        if (File.Exists(blob)) return blob;
        var part = $"{blob}.{Environment.ProcessId}.part";
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        try
        {
            using (var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!res.IsSuccessStatusCode) throw new ShimException(1, "source_download_failed", $"Downloading {url} failed with HTTP {(int)res.StatusCode}");
                await using var fs = File.Create(part);
                await res.Content.CopyToAsync(fs, ct);
            }
            await using (var check = File.OpenRead(part))
            {
                var got = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct));
                if (sha.Length == 64 && got != sha) throw new ShimException(1, "source_download_failed", $"{url}: sha256 {got} does not match {sha}");
            }
            File.Move(part, blob, overwrite: true);
            return blob;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
        {
            throw new ShimException(1, "source_download_failed", $"Downloading {url} failed: {e.Message}");
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }
}
