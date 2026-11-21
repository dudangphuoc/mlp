using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

/// <summary>For WatchNotifications: records changed after `since` + unread count.</summary>
[RequiresPermission("dataset.read")]
public sealed record NotificationsChangedQuery(Guid WorkspaceId, DateTimeOffset Since) : IQuery<(IReadOnlyList<NotificationRow> Items, long Unread)>;

public sealed class NotificationsChangedQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<NotificationsChangedQuery, (IReadOnlyList<NotificationRow> Items, long Unread)>
{
    public async ValueTask<(IReadOnlyList<NotificationRow> Items, long Unread)> Handle(NotificationsChangedQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return (await db.ListChangedSinceAsync(uid, q.WorkspaceId, q.Since, ct), await db.CountUnreadAsync(uid, q.WorkspaceId, ct));
    }
}
