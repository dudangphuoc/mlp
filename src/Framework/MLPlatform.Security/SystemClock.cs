using System.Security.Cryptography;
using System.Text;

namespace MLPlatform.Security;

public sealed class SystemClock(TimeProvider time) : IClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();
}
