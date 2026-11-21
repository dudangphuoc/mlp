using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Commit a version (M04 R5, R6): the latest version, minus the paths marked for removal, plus the files added
/// since (the working copy).</summary>
[RequiresPermission("dataset.write")]
public sealed record CreateVersionCommand(Guid WorkspaceId, Guid DatasetId, string Note) : ICommand<VersionRow>;

public sealed class CreateVersionCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateVersionCommand, VersionRow>
{
    public async ValueTask<VersionRow> Handle(CreateVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var files = await db.ListFilesAsync(c.DatasetId, ct);
        var removed = (await db.ListPendingRemovalsAsync(c.DatasetId, ct)).Select(r => r.Path).ToList();
        var baseVersion = await db.LatestVersionAsync(c.DatasetId, c.WorkspaceId, ct);
        if (files.Count == 0 && (removed.Count == 0 || baseVersion is null))
            throw new PreconditionException("DATA_NOTHING_TO_FREEZE", baseVersion is null ? "Dataset has no files yet" : "No changes since the latest version");
        var n = await db.NextVersionNumberAsync(c.DatasetId, c.WorkspaceId, ct);
        var prefix = Buckets.Version(c.WorkspaceId, c.DatasetId, n);
        foreach (var f in files)
            await s3.CopyAsync(Buckets.Datasets, Buckets.Raw(c.WorkspaceId, c.DatasetId) + f.Path, Buckets.Datasets, prefix + f.Path, ct);
        var id = Ids.New();
        var now = clock.UtcNow;
        await db.InsertVersionAsync(id, c.DatasetId, n, prefix, 0, 0, c.Note, uid, now, ct);
        foreach (var f in files) await db.InsertVersionFileAsync(id, f.Path, f.SizeBytes, ct);
        if (baseVersion is not null)
        {
            string[] skip = [.. files.Select(f => f.Path), .. removed, SplitRules.File];
            await db.InsertVersionFilesFromBaseAsync(id, baseVersion.Id, $"s3://{Buckets.Datasets}/{baseVersion.Prefix}", skip, ct);
        }
        await db.SetVersionTotalsAsync(id, ct);
        await db.ClearPendingChangesAsync(c.DatasetId, ct);
        return (await db.GetVersionAsync(id, c.WorkspaceId, ct))!;
    }
}
