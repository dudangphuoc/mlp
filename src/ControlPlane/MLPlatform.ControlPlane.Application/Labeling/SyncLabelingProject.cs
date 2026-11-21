using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>
/// Bring a project up to the dataset's latest version: the project now labels that version and its new files become items. Items and labels
/// already made stay.
/// </summary>
public sealed record SyncLabelingProjectCommand(Guid WorkspaceId, Guid ProjectId) : ICommand<long>;

/// <summary>Moves the project to the latest version and seeds what is new; returns how many items were added.</summary>
public sealed class SyncLabelingProjectCommandHandler(IWriteSession db, LabelAccess access) : ICommandHandler<SyncLabelingProjectCommand, long>
{
    public async ValueTask<long> Handle(SyncLabelingProjectCommand c, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        var ds = await db.GetDatasetAsync(p.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var latest = await db.LatestVersionAsync(p.DatasetId, c.WorkspaceId, ct) ?? throw new PreconditionException("LABEL_VERSION_REQUIRED", "Freeze a version of the dataset first");
        if (latest.Id != p.VersionId) await db.SetProjectVersionAsync(p.Id, c.WorkspaceId, latest.Id, ct);
        return await LabelSeeding.SeedAsync(db, p.Id, p.DatasetId, latest.Id, ds.Task, ct);
    }
}
