using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Hub module queries (hub schema).</summary>
public static partial class HubSql
{
    [SqlQuery("Hub/UpsertIntegration.sql")]
    public static partial Task UpsertIntegrationAsync(this IWriteSession db, Guid workspaceId, string kind, string secretEnc, string? account, string status, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Hub/GetIntegration.sql")]
    public static partial Task<IntegrationRow?> GetIntegrationAsync(this IReadSession db, Guid workspaceId, string kind, CancellationToken ct);
    [SqlQuery("Hub/DeleteIntegration.sql")]
    public static partial Task DeleteIntegrationAsync(this IWriteSession db, Guid workspaceId, string kind, CancellationToken ct);
    [SqlQuery("Hub/InsertImport.sql")]
    public static partial Task InsertImportAsync(this IWriteSession db, Guid id, Guid workspaceId, string kind, string repo, string revision, string[] include,
        Guid? targetId, string targetName, string task, Guid createdBy, DateTimeOffset now, string source, string mode, CancellationToken ct);
    [SqlQuery("Hub/GetImport.sql")]
    public static partial Task<HubImportJob?> GetImportAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Hub/UpdateImport.sql")]
    public static partial Task UpdateImportAsync(this IWriteSession db, Guid id, string state, Guid? targetId, int filesTotal, int filesDone, long bytesTotal, long bytesDone,
        string message, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Hub/ListImports.sql")]
    public static partial Task<List<HubImportRow>> ListImportsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Hub/FailStaleImports.sql")]
    public static partial Task<long> FailStaleImportsAsync(this IWriteSession db, DateTimeOffset now, CancellationToken ct);
}
