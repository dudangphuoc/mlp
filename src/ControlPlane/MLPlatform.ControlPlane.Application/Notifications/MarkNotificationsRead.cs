using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

/// <summary>Marks as read (a list of ids, or all).</summary>
public sealed record MarkNotificationsReadCommand(Guid WorkspaceId, IReadOnlyList<Guid> Ids, bool All) : ICommand<long>;

public sealed class MarkNotificationsReadCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<MarkNotificationsReadCommand, long>
{
    public async ValueTask<long> Handle(MarkNotificationsReadCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.read", ct);
        await db.MarkReadAsync(uid, c.WorkspaceId, c.All, c.Ids.ToArray(), clock.UtcNow, ct);
        return await db.CountUnreadAsync(uid, c.WorkspaceId, ct);
    }
}
