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

[Access(AccessKind.ScopedPermission, "labeling.annotate")]
public sealed record HeartbeatLabelLockCommand(Guid WorkspaceId, Guid ProjectId, string Path) : ICommand<DateTimeOffset>;

public sealed class HeartbeatLabelLockCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<HeartbeatLabelLockCommand, DateTimeOffset>
{
    public async ValueTask<DateTimeOffset> Handle(HeartbeatLabelLockCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        access.RequireLock(s, uid);
        var exp = clock.UtcNow.AddMinutes(p.LockTtlMin);
        await db.ExtendLockAsync(p.Id, c.Path, uid, exp, ct);
        return exp;
    }
}
