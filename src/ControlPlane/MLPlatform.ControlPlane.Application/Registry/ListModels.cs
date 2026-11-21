using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

public sealed record ListModelsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ModelListRow>>;

public sealed class ListModelsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListModelsQuery, IReadOnlyList<ModelListRow>>
{
    public async ValueTask<IReadOnlyList<ModelListRow>> Handle(ListModelsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListModelsAsync(q.WorkspaceId, ct);
    }
}
