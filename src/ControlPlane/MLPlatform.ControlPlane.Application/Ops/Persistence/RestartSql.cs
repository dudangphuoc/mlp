using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

public static partial class RestartSql
{
    [SqlQuery("Ops/RearmAttempts.sql")]
    public static partial Task RearmAttemptsAsync(this IWriteSession db, DateTimeOffset now, DateTimeOffset until, CancellationToken ct);
    [SqlQuery("Fleet/MarkHostsWaiting.sql")]
    public static partial Task MarkHostsWaitingAsync(this IWriteSession db, CancellationToken ct);
    [SqlQuery("Nb/RearmServers.sql")]
    public static partial Task RearmServersAsync(this IWriteSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Serving/RearmDeployments.sql")]
    public static partial Task RearmDeploymentsAsync(this IWriteSession db, DateTimeOffset now, CancellationToken ct);
}
