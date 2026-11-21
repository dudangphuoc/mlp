using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

public sealed record GetNotificationPreferencesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<PreferenceView>>;

public sealed class GetNotificationPreferencesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetNotificationPreferencesQuery, IReadOnlyList<PreferenceView>>
{
    public async ValueTask<IReadOnlyList<PreferenceView>> Handle(GetNotificationPreferencesQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        var off = (await db.ListPreferencesAsync(uid, q.WorkspaceId, ct)).Where(p => !p.Enabled).Select(p => p.Kind).ToHashSet();
        return NotificationKinds.All.Select(k => new PreferenceView(k.Key, k.Group, k.Label, k.Mandatory, k.Mandatory || !off.Contains(k.Key))).ToList();
    }
}
