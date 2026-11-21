using System.Security.Cryptography;
using System.Text;

namespace MLPlatform.Security;

/// <summary>Opaque tokens: prefix + 40 hex; stores SHA-256 + pepper (20/06 §1, 20/07 §1).</summary>
public sealed class TokenService(byte[] pepper)
{
    public static string New(string prefix) => prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20));

    public string Hash(string token) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(pepper, Encoding.UTF8.GetBytes(token)));
}
