using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>SQL of the model conversions.</summary>
public static partial class ExportSql
{
    [SqlQuery("Registry/InsertExport.sql")]
    public static partial Task InsertExportAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid modelVersionId, string format, Guid jobId,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/ListExports.sql")]
    public static partial Task<List<ExportRow>> ListExportsAsync(this IReadSession db, Guid workspaceId, Guid modelVersionId, CancellationToken ct);
    [SqlQuery("Registry/GetExport.sql")]
    public static partial Task<ExportRow?> GetExportAsync(this IReadSession db, Guid workspaceId, Guid id, CancellationToken ct);
    [SqlQuery("Registry/FinishExport.sql")]
    public static partial Task FinishExportAsync(this IWriteSession db, Guid workspaceId, Guid id, string state, string error, string fileName,
        long sizeBytes, CancellationToken ct);
    [SqlQuery("Registry/DeleteExport.sql")]
    public static partial Task DeleteExportAsync(this IWriteSession db, Guid workspaceId, Guid id, CancellationToken ct);
}
