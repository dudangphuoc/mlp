using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public sealed record ListNotebooksQuery(Guid WorkspaceId, bool All) : IQuery<IReadOnlyList<ServerRow>>;

public sealed class ListNotebooksQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListNotebooksQuery, IReadOnlyList<ServerRow>>
{
    public async ValueTask<IReadOnlyList<ServerRow>> Handle(ListNotebooksQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "notebook.use", ct);
        var all = q.All && await auth.HasAsync(q.WorkspaceId, "notebook.admin", ct);
        return await db.ListServersAsync(q.WorkspaceId, uid, all, ct);
    }
}
