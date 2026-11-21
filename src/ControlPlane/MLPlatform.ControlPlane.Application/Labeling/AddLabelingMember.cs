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

public sealed record AddLabelingMemberCommand(Guid WorkspaceId, Guid ProjectId, Guid UserId, string Role, int MaxOpenItems) : ICommand<bool>;

public sealed class AddLabelingMemberCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<AddLabelingMemberCommand, bool>
{
    public async ValueTask<bool> Handle(AddLabelingMemberCommand c, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        if (!LabelRules.Roles.Contains(c.Role)) throw LabelRules.Invalid("role", "lead | annotator | reviewer");
        _ = await db.GetMemberRoleAsync(c.WorkspaceId, c.UserId, ct) ?? throw new NotFoundException("IAM_MEMBER_NOT_FOUND", "This person is not a member of the workspace");
        await db.UpsertMemberAsync(c.ProjectId, c.UserId, c.Role, c.MaxOpenItems > 0 ? c.MaxOpenItems : 50, ct);
        await Notifier.SendAsync(db, clock, c.WorkspaceId, [c.UserId], "labeling.assigned",
            $"You were assigned the {c.Role} role in \"{p.Name}\"", $"#/labeling/{p.Id}", $"labelproject/{p.Id}", "info", ct);
        return true;
    }
}
