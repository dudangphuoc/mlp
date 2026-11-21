using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Registry;

public sealed record ModelRow(Guid Id, Guid WorkspaceId, string Name, string Task, DateTimeOffset CreatedAt);
public sealed record ModelListRow(Guid Id, Guid WorkspaceId, string Name, string Task, DateTimeOffset CreatedAt, int LatestVersion, int ProductionVersion);
public sealed record ModelVersionRow(Guid Id, Guid ModelId, string ModelName, int Version, Guid? RunId, string Recipe, string Variant, string Task,
    string Stage, long FileCount, long SizeBytes, string Metrics, DateTimeOffset CreatedAt, string Source, string? SourceRef, string Storage,
    Guid? ParentVersionId = null, string? Optimization = null);
public sealed record IdRow(Guid Id);

public static partial class RegistrySql
{
    [SqlQuery("Registry/ForgetDeployment.sql")]
    public static partial Task ForgetDeploymentAsync(this IWriteSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Registry/CountVersionsByRun.sql")]
    public static partial Task<long> CountVersionsByRunAsync(this IReadSession db, Guid runId, CancellationToken ct);
    [SqlQuery("Registry/GetModelByName.sql")]
    public static partial Task<ModelRow?> GetModelByNameAsync(this IReadSession db, Guid workspaceId, string name, CancellationToken ct);
    [SqlQuery("Registry/InsertModel.sql")]
    public static partial Task InsertModelAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, string task, string description,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/NextModelVersion.sql")]
    public static partial Task<int> NextModelVersionAsync(this IWriteSession db, Guid modelId, CancellationToken ct);
    [SqlQuery("Registry/InsertModelVersion.sql")]
    public static partial Task InsertModelVersionAsync(this IWriteSession db, Guid id, Guid modelId, int version, Guid runId, string recipe,
        string variant, string task, string prefix, long fileCount, long sizeBytes, string metrics, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/ListModels.sql")]
    public static partial Task<List<ModelListRow>> ListModelsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/ListModelVersions.sql")]
    public static partial Task<List<ModelVersionRow>> ListModelVersionsAsync(this IReadSession db, Guid modelId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/GetModelVersion.sql")]
    public static partial Task<ModelVersionRow?> GetModelVersionAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/ListVersionsByRun.sql")]
    public static partial Task<List<ModelVersionRow>> ListVersionsByRunAsync(this IReadSession db, Guid runId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/SetStage.sql")]
    public static partial Task SetStageAsync(this IWriteSession db, Guid id, string stage, CancellationToken ct);
    [SqlQuery("Registry/ArchiveOtherInStage.sql")]
    public static partial Task<List<IdRow>> ArchiveOtherInStageAsync(this IWriteSession db, Guid modelId, string stage, Guid keepId, CancellationToken ct);
    [SqlQuery("Registry/InsertStageTransition.sql")]
    public static partial Task InsertStageTransitionAsync(this IWriteSession db, Guid id, Guid versionId, string fromStage, string toStage,
        Guid actorId, string reason, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Registry/GetVersionPrefix.sql")]
    public static partial Task<string?> GetVersionPrefixAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
}
