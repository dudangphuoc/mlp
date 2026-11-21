using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Invitations of the workspace.</summary>
[RequiresPermission("member.manage")]
public sealed record ListInvitationsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<InvitationListRow>>;

/// <summary>Handles <see cref="ListInvitationsQuery"/>.</summary>
public sealed class ListInvitationsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListInvitationsQuery, IReadOnlyList<InvitationListRow>>
{
    public async ValueTask<IReadOnlyList<InvitationListRow>> Handle(ListInvitationsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "member.manage", ct);
        return await db.ListInvitationsAsync(q.WorkspaceId, ct);
    }
}
