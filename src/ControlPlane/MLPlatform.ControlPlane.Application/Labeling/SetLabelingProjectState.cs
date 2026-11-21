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

/// <summary>activate (seeds items from the dataset's files) / pause / complete.</summary>
public sealed record SetLabelingProjectStateCommand(Guid WorkspaceId, Guid ProjectId, string State) : ICommand<ProjectRow>;

public sealed class SetLabelingProjectStateCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SetLabelingProjectStateCommand, ProjectRow>
{
    public async ValueTask<ProjectRow> Handle(SetLabelingProjectStateCommand c, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        _ = roles;
        var ok = (p.State, c.State) switch
        {
            ("draft" or "paused" or "completed", "active") => true,
            ("active", "paused") => true,
            ("active" or "paused", "completed") => true,
            _ => false,
        };
        if (!ok) throw new PreconditionException("LABEL_PROJECT_STATE", $"Cannot change state {p.State} → {c.State}");
        if (c.State == "active")
        {
            var task = (await db.GetDatasetAsync(p.DatasetId, p.WorkspaceId, ct))?.Task ?? "";
            var versionId = p.VersionId;
            if (versionId is null && await db.LatestVersionAsync(p.DatasetId, c.WorkspaceId, ct) is { } latest)
            {
                await db.SetProjectVersionAsync(p.Id, c.WorkspaceId, latest.Id, ct);
                versionId = latest.Id;
            }
            await LabelSeeding.SeedAsync(db, p.Id, p.DatasetId, versionId, task, ct);
        }
        await db.SetProjectStateAsync(p.Id, c.State, ct);
        if (c.State == "completed")
            await Notifier.SendAsync(db, clock, p.WorkspaceId,
                (await db.ListProjectMembersAsync(p.Id, ct)).Where(m => m.Role == "lead").Select(m => m.UserId).Concat(await Notifier.AdminsAsync(db, p.WorkspaceId, ct)),
                "labeling.project_completed", $"Labeling project \"{p.Name}\" is complete", $"#/labeling/{p.Id}", $"labelproject/{p.Id}", "success", ct);
        return (await db.GetLabelProjectAsync(p.Id, c.WorkspaceId, ct))!;
    }
}
