using System.Security.Cryptography;
using System.Text;

namespace MLPlatform.Security;

/// <summary>The single source of "now" (20/07 §2).</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
