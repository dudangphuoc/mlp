using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

// ───────────── RPC ─────────────

[RequiresPermission("dataset.read")]
public sealed record ListNotificationsQuery(Guid WorkspaceId, bool UnreadOnly) : IQuery<(IReadOnlyList<NotificationRow> Items, long Unread)>;

public sealed class ListNotificationsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListNotificationsQuery, (IReadOnlyList<NotificationRow> Items, long Unread)>
{
    public async ValueTask<(IReadOnlyList<NotificationRow> Items, long Unread)> Handle(ListNotificationsQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return (await db.ListNotificationsAsync(uid, q.WorkspaceId, q.UnreadOnly, ct), await db.CountUnreadAsync(uid, q.WorkspaceId, ct));
    }
}
