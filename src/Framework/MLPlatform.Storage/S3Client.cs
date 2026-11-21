using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace MLPlatform.Storage;

/// <summary>Minimal, AOT-safe S3 client (D-34): SigV4 for requests and presigned URLs, path-style (
/// {endpoint}/{bucket}/{key} ) so it works with SeaweedFS/MinIO/Ceph/real S3.</summary>
public sealed class S3Client(HttpClient http, S3Options o, TimeProvider time)
{
    private const string Service = "s3";
    private const string EmptySha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public async Task EnsureBucketAsync(string bucket, CancellationToken ct)
    {
        using var head = await SendAsync(HttpMethod.Head, bucket, "", null, null, ct);
        if (head.IsSuccessStatusCode) return;
        using var put = await SendAsync(HttpMethod.Put, bucket, "", null, null, ct);
        if (!put.IsSuccessStatusCode && put.StatusCode != HttpStatusCode.Conflict)
            throw new S3Exception($"Creating bucket {bucket}: {(int)put.StatusCode} {await put.Content.ReadAsStringAsync(ct)}");
    }

    public async Task PutAsync(string bucket, string key, byte[] body, string contentType, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Put, bucket, key, null, body, ct, contentType);
        if (!res.IsSuccessStatusCode) throw new S3Exception($"PUT {bucket}/{key}: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync(ct)}");
    }

    public async Task<byte[]?> GetAsync(string bucket, string key, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Get, bucket, key, null, null, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        if (!res.IsSuccessStatusCode) throw new S3Exception($"GET {bucket}/{key}: {(int)res.StatusCode}");
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<S3Object?> HeadAsync(string bucket, string key, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Head, bucket, key, null, null, ct);
        if (!res.IsSuccessStatusCode) return null;
        return new S3Object(key, res.Content.Headers.ContentLength ?? 0, res.Content.Headers.LastModified ?? DateTimeOffset.MinValue);
    }

    /// <summary>Server-side copy (used when committing a dataset version; versions are immutable, 20/07 §5).</summary>
    public async Task CopyAsync(string srcBucket, string srcKey, string bucket, string key, CancellationToken ct)
    {
        var src = "/" + srcBucket + "/" + string.Join('/', srcKey.Split('/').Select(Uri.EscapeDataString));
        using var res = await SendAsync(HttpMethod.Put, bucket, key, null, null, ct, extraHeaders: new() { ["x-amz-copy-source"] = src });
        if (!res.IsSuccessStatusCode) throw new S3Exception($"COPY {srcKey} → {key}: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync(ct)}");
    }

    public async Task DeleteAsync(string bucket, string key, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Delete, bucket, key, null, null, ct);
    }

    public async Task<List<S3Object>> ListAsync(string bucket, string prefix, CancellationToken ct)
    {
        var all = new List<S3Object>();
        string? token = null;
        do
        {
            var q = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["list-type"] = "2", ["prefix"] = prefix };
            if (token is not null) q["continuation-token"] = token;
            using var res = await SendAsync(HttpMethod.Get, bucket, "", q, null, ct);
            if (!res.IsSuccessStatusCode) throw new S3Exception($"LIST {bucket}/{prefix}: {(int)res.StatusCode}");
            var doc = XDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            XNamespace ns = doc.Root!.Name.Namespace;
            foreach (var c in doc.Root.Elements(ns + "Contents"))
                all.Add(new S3Object(c.Element(ns + "Key")!.Value, long.Parse(c.Element(ns + "Size")!.Value, CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(c.Element(ns + "LastModified")!.Value, CultureInfo.InvariantCulture)));
            token = doc.Root.Element(ns + "IsTruncated")?.Value == "true" ? doc.Root.Element(ns + "NextContinuationToken")?.Value : null;
        } while (token is not null);
        return all;
    }

    /// <summary>Presigned URL (SigV4 query) for browsers/agents to upload or download directly (20/05: the api only
    /// issues URLs).</summary>
    public string Presign(HttpMethod method, string bucket, string key, TimeSpan expires, Audience audience = Audience.Public)
    {
        var endpoint = new Uri(audience == Audience.Internal ? o.Endpoint : (audience == Audience.Workload ? o.WorkloadEndpoint : null) ?? o.PublicEndpoint ?? o.Endpoint);
        var now = time.GetUtcNow();
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var scope = $"{date}/{o.Region}/{Service}/aws4_request";
        var path = CanonicalPath(endpoint, bucket, key);
        var q = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = "AWS4-HMAC-SHA256",
            ["X-Amz-Credential"] = $"{o.AccessKey}/{scope}",
            ["X-Amz-Date"] = amzDate,
            ["X-Amz-Expires"] = ((int)expires.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            ["X-Amz-SignedHeaders"] = "host",
        };
        var canonicalQuery = Query(q);
        var host = HostHeader(endpoint);
        var canonical = $"{method.Method}\n{path}\n{canonicalQuery}\nhost:{host}\n\nhost\nUNSIGNED-PAYLOAD";
        var sig = Sign(date, $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}");
        return $"{endpoint.GetLeftPart(UriPartial.Authority)}{path}?{canonicalQuery}&X-Amz-Signature={sig}";
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string bucket, string key, SortedDictionary<string, string>? query,
        byte[]? body, CancellationToken ct, string? contentType = null, SortedDictionary<string, string>? extraHeaders = null)
    {
        var endpoint = new Uri(o.Endpoint);
        var now = time.GetUtcNow();
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var path = CanonicalPath(endpoint, bucket, key);
        var canonicalQuery = query is null ? "" : Query(query);
        var payloadHash = body is null ? EmptySha : Hex(SHA256.HashData(body));
        var host = HostHeader(endpoint);
        var signed = new SortedDictionary<string, string>(StringComparer.Ordinal)
            { ["host"] = host, ["x-amz-content-sha256"] = payloadHash, ["x-amz-date"] = amzDate };
        foreach (var (k, v) in extraHeaders ?? []) signed[k.ToLowerInvariant()] = v;
        var signedNames = string.Join(';', signed.Keys);
        var canonical = $"{method.Method}\n{path}\n{canonicalQuery}\n{string.Concat(signed.Select(kv => kv.Key + ":" + kv.Value + "\n"))}\n{signedNames}\n{payloadHash}";
        var scope = $"{date}/{o.Region}/{Service}/aws4_request";
        var sig = Sign(date, $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}");
        var req = new HttpRequestMessage(method, $"{endpoint.GetLeftPart(UriPartial.Authority)}{path}{(canonicalQuery.Length > 0 ? "?" + canonicalQuery : "")}");
        req.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        req.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        req.Headers.TryAddWithoutValidation("Authorization",
            $"AWS4-HMAC-SHA256 Credential={o.AccessKey}/{scope}, SignedHeaders={signedNames}, Signature={sig}");
        foreach (var (k, v) in extraHeaders ?? []) req.Headers.TryAddWithoutValidation(k, v);
        if (body is not null)
        {
            req.Content = new ByteArrayContent(body);
            req.Content.Headers.TryAddWithoutValidation("Content-Type", contentType ?? "application/octet-stream");
        }
        return await http.SendAsync(req, ct);
    }

    private string Sign(string date, string stringToSign)
    {
        var k = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + o.SecretKey), Encoding.UTF8.GetBytes(date));
        k = HMACSHA256.HashData(k, Encoding.UTF8.GetBytes(o.Region));
        k = HMACSHA256.HashData(k, Encoding.UTF8.GetBytes(Service));
        k = HMACSHA256.HashData(k, Encoding.UTF8.GetBytes("aws4_request"));
        return Hex(HMACSHA256.HashData(k, Encoding.UTF8.GetBytes(stringToSign)));
    }

    private static string CanonicalPath(Uri endpoint, string bucket, string key)
    {
        var basePath = endpoint.AbsolutePath.TrimEnd('/');
        var segs = new[] { bucket }.Concat(key.Length > 0 ? key.Split('/') : []).Select(Uri.EscapeDataString);
        return basePath + "/" + string.Join('/', segs);
    }

    private static string HostHeader(Uri u) => u.IsDefaultPort ? u.Host : $"{u.Host}:{u.Port}";

    private static string Query(SortedDictionary<string, string> q) =>
        string.Join('&', q.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));

    private static string Hex(byte[] b) => Convert.ToHexStringLower(b);
}
