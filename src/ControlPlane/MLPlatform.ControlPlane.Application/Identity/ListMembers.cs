using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

public sealed record ListMembersQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<MemberRow>>;

public sealed class ListMembersQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListMembersQuery, IReadOnlyList<MemberRow>>
{
    public async ValueTask<IReadOnlyList<MemberRow>> Handle(ListMembersQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListMembersAsync(q.WorkspaceId, ct);
    }
}
