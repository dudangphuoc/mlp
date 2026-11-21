using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>SQL of model optimization.</summary>
public static partial class OptimizationSql
{
    [SqlQuery("Registry/InsertOptimization.sql")]
    public static partial Task InsertOptimizationAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid sourceVersionId, Guid exportId, string device,
        string candidates, Guid? hostId, Guid? calibDatasetVersionId, Guid checkDatasetVersionId, double conf, Guid jobId, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/ListOptimizations.sql")]
    public static partial Task<List<OptimizationRow>> ListOptimizationsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/GetOptimization.sql")]
    public static partial Task<OptimizationRow?> GetOptimizationAsync(this IReadSession db, Guid workspaceId, Guid id, CancellationToken ct);
    [SqlQuery("Registry/FinishOptimization.sql")]
    public static partial Task FinishOptimizationAsync(this IWriteSession db, Guid workspaceId, Guid id, string state, string error, string report, CancellationToken ct);
    [SqlQuery("Registry/SetOptimizationPromoted.sql")]
    public static partial Task SetOptimizationPromotedAsync(this IWriteSession db, Guid workspaceId, Guid id, string candidate, Guid versionId, CancellationToken ct);
    [SqlQuery("Registry/DeleteOptimization.sql")]
    public static partial Task DeleteOptimizationAsync(this IWriteSession db, Guid workspaceId, Guid id, CancellationToken ct);
    [SqlQuery("Registry/InsertOptimizedVersion.sql")]
    public static partial Task InsertOptimizedVersionAsync(this IWriteSession db, Guid id, Guid modelId, int version, Guid? runId, string task, string prefix,
        long fileCount, long sizeBytes, Guid createdBy, DateTimeOffset now, string sourceRef, Guid parentVersionId, string optimization, CancellationToken ct);
    [SqlQuery("Registry/GetVersionDevice.sql")]
    public static partial Task<string?> GetVersionDeviceAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/GetModelVersionModelId.sql")]
    public static partial Task<Guid?> GetModelVersionModelIdAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/ListReadyOnnxExports.sql")]
    public static partial Task<List<ExportRow>> ListReadyOnnxExportsAsync(this IReadSession db, Guid workspaceId, Guid modelVersionId, CancellationToken ct);
}
