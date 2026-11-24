using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

[Access(AccessKind.Authenticated)]
public sealed record ListWorkspacesQuery : IQuery<IReadOnlyList<MembershipRow>>;

public sealed class ListWorkspacesQueryHandler(IReadSession db, ICurrentUser user)
    : IQueryHandler<ListWorkspacesQuery, IReadOnlyList<MembershipRow>>
{
    public async ValueTask<IReadOnlyList<MembershipRow>> Handle(ListWorkspacesQuery q, CancellationToken ct)
    {
        var all = await db.ListMembershipsAsync(user.RequireUser(), ct);
        return user.KeyWorkspaceId is { } only ? all.Where(m => m.Id == only).ToList() : all;
    }
}
