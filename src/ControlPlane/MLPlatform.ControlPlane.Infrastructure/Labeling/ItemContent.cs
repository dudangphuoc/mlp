using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Reads an item's bytes (dataset storage or the reference source), with a size cap.</summary>
public sealed class ItemContent(S3Client s3, IHttpClientFactory http)
{
    public async Task<(byte[]? Bytes, string? Error)> ReadAsync(HermesSource src, int maxBytes, CancellationToken ct)
    {
        try
        {
            if (src.ExternalUrl is { } url)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15));
                using var res = await http.CreateClient("import").GetAsync(MLPlatform.ControlPlane.Application.Common.SourceUrls.Resolve(s3, url),
                    HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!res.IsSuccessStatusCode) return (null, $"HTTP {(int)res.StatusCode} from the source");
                if (res.Content.Headers.ContentLength > maxBytes) return (null, $"file is larger than {maxBytes / 1024 / 1024} MB");
                await using var st = await res.Content.ReadAsStreamAsync(cts.Token);
                var buf = new byte[maxBytes + 1];
                var read = 0;
                int n;
                while (read < buf.Length && (n = await st.ReadAsync(buf.AsMemory(read), cts.Token)) > 0) read += n;
                return read > maxBytes ? (null, $"file is larger than {maxBytes / 1024 / 1024} MB") : (buf[..read], null);
            }
            var head = await s3.HeadAsync(src.Bucket!, src.Key!, ct);
            if (head is null) return (null, "file not found in storage");
            if (head.Size > maxBytes) return (null, $"file is {head.Size / 1024 / 1024} MB; the limit is {maxBytes / 1024 / 1024} MB");
            var bytes = await s3.GetAsync(src.Bucket!, src.Key!, ct);
            return bytes is null ? (null, "file not found in storage") : (bytes, null);
        }
        catch (Exception e) when (e is HttpRequestException or S3Exception or TaskCanceledException && !ct.IsCancellationRequested) { return (null, e.Message); }
    }

    public async Task<(string? Text, string? Note)> ReadTextAsync(HermesSource src, int maxChars, CancellationToken ct)
    {
        var (bytes, err) = await ReadAsync(src, 2 * 1024 * 1024, ct);
        if (bytes is null) return (null, err);
        var text = Encoding.UTF8.GetString(bytes);
        if (maxChars < 500) maxChars = 500;
        return text.Length <= maxChars ? (text, null) : (text[..maxChars], $"text cut to the first {maxChars:N0} of {text.Length:N0} characters to fit the model");
    }

    public static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", ".bmp" => "image/bmp", _ => "image/jpeg",
    };
}
