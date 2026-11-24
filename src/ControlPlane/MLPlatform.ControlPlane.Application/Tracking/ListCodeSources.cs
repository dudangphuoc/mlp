using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Git URL prefixes the server fetches code from.</summary>
[Access(AccessKind.ScopedPermission, "run.read")]
public sealed record ListCodeSourcesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<CodeSourceRow>>;

public sealed class ListCodeSourcesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListCodeSourcesQuery, IReadOnlyList<CodeSourceRow>>
{
    public async ValueTask<IReadOnlyList<CodeSourceRow>> Handle(ListCodeSourcesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListCodeSourcesAsync(q.WorkspaceId, ct);
    }
}
