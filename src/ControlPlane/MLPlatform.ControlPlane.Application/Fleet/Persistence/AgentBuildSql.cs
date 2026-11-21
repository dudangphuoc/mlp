using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Query that checks the enroll token for the install flow (the script sends doctor results before it has a credential).</summary>
public static partial class AgentBuildSql
{
    [SqlQuery("Fleet/EnrollTokenValid.sql")]
    public static partial Task<long> EnrollTokenValidAsync(this IReadSession db, string tokenHash, DateTimeOffset now, CancellationToken ct);
}
