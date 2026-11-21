using System.Security.Cryptography;
using System.Text;

namespace MLPlatform.Security;

/// <summary>UUIDv7 (20/07 §1).</summary>
public static class Ids
{
    public static Guid New() => Guid.CreateVersion7();
}
