using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Who still uses a dataset version (each module answers for its own tables).</summary>
public static partial class VersionUsageSql
{
    [SqlQuery("Track/RunsUsingVersion.sql")] public static partial Task<long> RunsUsingVersionAsync(this IReadSession db, Guid versionId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Track/DraftsUsingVersion.sql")] public static partial Task<long> DraftsUsingVersionAsync(this IReadSession db, Guid versionId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/ProjectsUsingVersion.sql")] public static partial Task<long> ProjectsUsingVersionAsync(this IReadSession db, Guid versionId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Registry/EvaluationsUsingVersion.sql")] public static partial Task<long> EvaluationsUsingVersionAsync(this IReadSession db, Guid versionId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/DetachDeletedProjects.sql")]
    public static partial Task DetachDeletedProjectsAsync(this IWriteSession db, Guid versionId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/SetProjectsStateOfDataset.sql")]
    public static partial Task SetProjectsStateOfDatasetAsync(this IWriteSession db, Guid datasetId, Guid workspaceId, string state, CancellationToken ct);
}
