using System.Security.Cryptography;
using System.Text;

namespace MLPlatform.Security;

/// <summary>The single source of "now" (20/07 §2).</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock(TimeProvider time) : IClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();
}

/// <summary>PBKDF2-SHA256 passwords, 600,000 iterations, 16-byte salt, versioned string (20/06 §1).</summary>
public static class PasswordHasher
{
    private const int Iterations = 600_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "v1") return false;
        var iter = int.Parse(parts[1]);
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(key, expected);
    }
}

/// <summary>Opaque tokens: prefix + 40 hex; stores SHA-256 + pepper (20/06 §1, 20/07 §1).</summary>
public sealed class TokenService(byte[] pepper)
{
    public static string New(string prefix) => prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20));

    public string Hash(string token) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(pepper, Encoding.UTF8.GetBytes(token)));
}

/// <summary>UUIDv7 (20/07 §1).</summary>
public static class Ids
{
    public static Guid New() => Guid.CreateVersion7();
}
