using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public sealed record ListProfilesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ProfileRow>>;

public sealed class ListProfilesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListProfilesQuery, IReadOnlyList<ProfileRow>>
{
    public async ValueTask<IReadOnlyList<ProfileRow>> Handle(ListProfilesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListProfilesAsync(ct);
    }
}
