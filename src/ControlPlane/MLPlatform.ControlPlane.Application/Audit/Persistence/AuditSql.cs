using System.Security.Cryptography;
using System.Text;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Audit;

public static partial class AuditSql
{
    [SqlQuery("Audit/LockChain.sql")] public static partial Task LockChainAsync(this IWriteSession db, CancellationToken ct);
    [SqlQuery("Audit/LastHash.sql")] public static partial Task<string?> LastHashAsync(this IWriteSession db, CancellationToken ct);
    [SqlQuery("Audit/InsertEvent.sql")]
    public static partial Task InsertAuditEventAsync(this IWriteSession db, Guid id, DateTimeOffset at, Guid? workspaceId, Guid? actorId, string action,
        string outcome, string? reason, string prevHash, string hash, CancellationToken ct);
    [SqlQuery("Audit/ListEvents.sql")] public static partial Task<List<AuditRow>> ListAuditEventsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Audit/AllEvents.sql")] public static partial Task<List<AuditRow>> AllEventsAsync(this IReadSession db, CancellationToken ct);
}
