using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MLPlatform.Agent;

/// <summary>Agent channel TLS (P0-8, 20/04 §3): with CertPin (SHA-256 of the control plane certificate, printed in the
/// install command) only that exact certificate is accepted, so self-signed certificates work.</summary>
public static class AgentTls
{
    public static async Task<Stream> WrapAsync(Stream stream, AgentOptions o, string host, CancellationToken ct)
    {
        if (!o.UseTls) return stream;
        var pin = o.CertPin?.Replace(":", "").Replace("sha256", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        var ssl = new SslStream(stream, false, (_, cert, _, errors) =>
            pin is { Length: > 0 }
                ? cert is not null && Convert.ToHexStringLower(SHA256.HashData(cert.GetRawCertData())) == pin
                : errors == SslPolicyErrors.None);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, ct);
        return ssl;
    }

    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexStringLower(SHA256.HashData(cert.RawData));
}
