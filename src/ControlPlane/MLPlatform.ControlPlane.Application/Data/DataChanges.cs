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

/// <summary>
/// Remove files from what the next version will contain: a file added since the latest version is dropped at once (and its upload deleted);
/// a file of the latest version is marked, and leaves with the next version. Versions themselves never change.
/// </summary>
public sealed record RemoveDatasetFilesCommand(Guid WorkspaceId, Guid DatasetId, IReadOnlyList<string> Paths) : ICommand<int>;

/// <summary>Deletes pending uploads, marks frozen files; returns how many paths it took.</summary>
public sealed class RemoveDatasetFilesCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<RemoveDatasetFilesCommand, int>
{
    public async ValueTask<int> Handle(RemoveDatasetFilesCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        if (c.Paths.Count is 0 or > 10_000) throw new InvalidArgumentException("DATA_PATHS_INVALID", "Pick 1 to 10,000 files");
        var latest = await db.LatestVersionAsync(c.DatasetId, c.WorkspaceId, ct);
        var frozen = latest is null ? [] : (await db.FindFilesByPathAsync(latest.Id, c.DatasetId, [.. c.Paths.Select(Buckets.SafePath)], ct)).Select(f => f.Path).ToHashSet();
        var n = 0;
        foreach (var raw in c.Paths.Distinct())
        {
            var path = Buckets.SafePath(raw);
            if (await db.DeleteWorkingFileAsync(c.DatasetId, path, ct) > 0)
            {
                await s3.DeleteAsync(Buckets.Datasets, Buckets.Raw(c.WorkspaceId, c.DatasetId) + path, ct);
                n++;
            }
            if (frozen.Contains(path)) { await db.InsertPendingRemovalAsync(c.DatasetId, path, clock.UtcNow, ct); n++; }
        }
        return n;
    }
}

/// <summary>Undo "remove" for files of the latest version that are only marked.</summary>
public sealed record KeepDatasetFilesCommand(Guid WorkspaceId, Guid DatasetId, IReadOnlyList<string> Paths) : ICommand<bool>;

/// <summary>Drops the removal marks.</summary>
public sealed class KeepDatasetFilesCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<KeepDatasetFilesCommand, bool>
{
    public async ValueTask<bool> Handle(KeepDatasetFilesCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        foreach (var p in c.Paths) await db.DeletePendingRemovalAsync(c.DatasetId, Buckets.SafePath(p), ct);
        return true;
    }
}

/// <summary>Paths of the latest version that leave with the next one.</summary>
public sealed record ListPendingRemovalsQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<IReadOnlyList<string>>;

/// <summary>Reads the removal marks.</summary>
public sealed class ListPendingRemovalsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPendingRemovalsQuery, IReadOnlyList<string>>
{
    public async ValueTask<IReadOnlyList<string>> Handle(ListPendingRemovalsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return (await db.ListPendingRemovalsAsync(q.DatasetId, ct)).Select(r => r.Path).ToList();
    }
}

/// <summary>
/// Delete a dataset version that nothing uses (no run trained on it, no labeling project, draft or evaluation points at it). Its stored
/// files are deleted too, unless a later version still points at them.
/// </summary>
public sealed record DeleteVersionCommand(Guid WorkspaceId, Guid VersionId) : ICommand<bool>;

/// <summary>Refuses with what still uses the version.</summary>
public sealed class DeleteVersionCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<DeleteVersionCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteVersionCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        var v = await db.GetVersionAsync(c.VersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var users = new List<string>();
        if (await db.RunsUsingVersionAsync(v.Id, c.WorkspaceId, ct) is > 0 and var r) users.Add($"{r} training run(s)");
        if (await db.ProjectsUsingVersionAsync(v.Id, c.WorkspaceId, ct) is > 0 and var p) users.Add($"{p} labeling project(s)");
        if (await db.DraftsUsingVersionAsync(v.Id, c.WorkspaceId, ct) is > 0 and var d) users.Add($"{d} recipe draft(s)");
        if (await db.EvaluationsUsingVersionAsync(v.Id, c.WorkspaceId, ct) is > 0 and var e) users.Add($"{e} evaluation(s)");
        if (users.Count > 0) throw new PreconditionException("DATA_VERSION_IN_USE", $"Version v{v.Number} is used by " + string.Join(", ", users));
        var keepObjects = v.Prefix.Length == 0 || await db.VersionFilesReferencedAsync(v.Id, $"s3://{Buckets.Datasets}/{v.Prefix}", ct) > 0;
        await db.DetachDeletedProjectsAsync(v.Id, c.WorkspaceId, ct);
        await db.DeleteVersionAsync(v.Id, ct);
        if (!keepObjects)
            foreach (var o in await s3.ListAsync(Buckets.Datasets, v.Prefix, ct)) await s3.DeleteAsync(Buckets.Datasets, o.Key, ct);
        return true;
    }
}

/// <summary>
/// Delete a dataset: it leaves every list and its labeling projects end. Versions that trained runs stay readable for those runs
/// (a run keeps showing which data it used), so nothing is erased from storage here.
/// </summary>
public sealed record DeleteDatasetCommand(Guid WorkspaceId, Guid DatasetId) : ICommand<bool>;

/// <summary>Marks the dataset and its labeling projects deleted.</summary>
public sealed class DeleteDatasetCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteDatasetCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteDatasetCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        await db.SetDatasetStatusAsync(c.DatasetId, c.WorkspaceId, "deleted", ct);
        await db.SetProjectsStateOfDatasetAsync(c.DatasetId, c.WorkspaceId, "deleted", ct);
        return true;
    }
}
