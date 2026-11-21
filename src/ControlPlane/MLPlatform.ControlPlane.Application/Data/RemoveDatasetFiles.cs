using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Remove files from what the next version will contain: a file added since the latest version is dropped at
/// once (and its upload deleted); a file of the latest version is marked, and leaves with the next version.</summary>
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
