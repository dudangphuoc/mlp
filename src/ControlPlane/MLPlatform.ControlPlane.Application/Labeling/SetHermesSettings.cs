using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record SetHermesSettingsCommand(Guid WorkspaceId, Guid ProjectId, bool Enabled, double Threshold) : ICommand<ProjectRow>;

public sealed class SetHermesSettingsCommandHandler(IWriteSession db, LabelAccess access) : ICommandHandler<SetHermesSettingsCommand, ProjectRow>
{
    public async ValueTask<ProjectRow> Handle(SetHermesSettingsCommand c, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "dataset.read", ct);
        if (!roles.Contains("lead"))
            await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);   // not a Lead → needs the workspace right
        if (c.Threshold is < 0 or > 1) throw LabelRules.Invalid("threshold", "Threshold must be between 0 and 1");
        await db.SetHermesSettingsAsync(p.Id, p.WorkspaceId, c.Enabled, Math.Round(c.Threshold, 3), ct);
        return (await db.GetLabelProjectAsync(p.Id, p.WorkspaceId, ct))!;
    }
}
