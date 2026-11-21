using MLPlatform.ControlPlane.Application.Common;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Encryption of integration secrets (AES-GCM, key derived from the instance pepper); the DB stores only
/// ciphertext (M01 R10).</summary>
public sealed class SecretBox(IConfiguration cfg)
{
    private byte[] Key => HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg["Secrets:TokenPepper"] ?? "dev"), "mlp-integrations"u8);

    public string Seal(string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plain);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Key, 16)) aes.Encrypt(nonce, pt, ct, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. ct]);
    }

    public string? Open(string? sealedText)
    {
        if (string.IsNullOrEmpty(sealedText)) return null;
        try
        {
            var b = Convert.FromBase64String(sealedText);
            var pt = new byte[b.Length - 28];
            using var aes = new AesGcm(Key, 16);
            aes.Decrypt(b.AsSpan(0, 12), b.AsSpan(28), b.AsSpan(12, 16), pt);
            return Encoding.UTF8.GetString(pt);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException) { return null; }
    }
}
