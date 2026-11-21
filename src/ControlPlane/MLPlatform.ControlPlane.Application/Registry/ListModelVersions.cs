using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

public sealed record ListModelVersionsQuery(Guid WorkspaceId, Guid ModelId) : IQuery<IReadOnlyList<ModelVersionRow>>;

public sealed class ListModelVersionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListModelVersionsQuery, IReadOnlyList<ModelVersionRow>>
{
    public async ValueTask<IReadOnlyList<ModelVersionRow>> Handle(ListModelVersionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListModelVersionsAsync(q.ModelId, q.WorkspaceId, ct);
    }
}
