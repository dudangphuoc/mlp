using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

// ───────────── Project ─────────────

/// <summary>VersionId: label the files of that dataset version (copied or reference); null = the dataset's uploaded
/// files.</summary>
[RequiresPermission("labeling.manage")]
public sealed record CreateLabelingProjectCommand(Guid WorkspaceId, Guid DatasetId, string Name, string GuidelineMd, string ReviewMode, int LockTtlMin,
    Guid? VersionId = null, double AutoAcceptThreshold = 0) : ICommand<ProjectRow>;

public sealed class CreateLabelingProjectCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateLabelingProjectCommand, ProjectRow>
{
    public async ValueTask<ProjectRow> Handle(CreateLabelingProjectCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "labeling.manage", ct);
        var ds = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var schema = await db.GetCurrentSchemaAsync(c.DatasetId, ct) ?? throw new PreconditionException("LABEL_SCHEMA_REQUIRED", "Define a label schema first");
        var mode = string.IsNullOrEmpty(c.ReviewMode) ? "none" : c.ReviewMode;
        var versionId = c.VersionId ?? (await db.LatestVersionAsync(c.DatasetId, c.WorkspaceId, ct))?.Id
                        ?? throw new PreconditionException("LABEL_VERSION_REQUIRED", "Freeze a version of the dataset first");
        if (mode is not ("none" or "all")) throw LabelRules.Invalid("review_mode", "none | all");
        if (string.IsNullOrWhiteSpace(c.Name)) throw LabelRules.Invalid("name", "Project name is required");
        if ((await db.GetVersionAsync(versionId, c.WorkspaceId, ct))?.DatasetId != c.DatasetId)
            throw LabelRules.Invalid("version_id", "The version does not belong to this dataset");
        if (c.AutoAcceptThreshold is < 0 or > 1) throw LabelRules.Invalid("auto_accept_threshold", "Must be between 0 and 1 (0 = off)");
        var id = Ids.New();
        await db.InsertLabelProjectAsync(id, c.WorkspaceId, c.DatasetId, c.Name.Trim(), c.GuidelineMd, schema.Version, mode,
            c.LockTtlMin is > 0 and <= 240 ? c.LockTtlMin : 10, uid, clock.UtcNow, versionId, c.AutoAcceptThreshold, ct);
        await db.UpsertMemberAsync(id, uid, "lead", 50, ct);
        await db.UpsertMemberAsync(id, uid, "annotator", 500, ct);
        await LabelSeeding.SeedAsync(db, id, c.DatasetId, versionId, ds.Task, ct);
        await db.SetProjectStateAsync(id, "active", ct);
        return (await db.GetLabelProjectAsync(id, c.WorkspaceId, ct))!;
    }
}
