using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Security;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Machine Docker health queries.</summary>
public static partial class HostDockerSql
{
    [SqlQuery("Fleet/SetHostDocker.sql")]
    public static partial Task<bool?> SetHostDockerAsync(this IWriteSession db, Guid id, bool ok, string error, CancellationToken ct);
}
