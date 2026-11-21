using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

[RequiresPermission("dataset.read")]
public sealed record ListEntitiesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<EntityRow>>;

public sealed class ListEntitiesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEntitiesQuery, IReadOnlyList<EntityRow>>
{
    public async ValueTask<IReadOnlyList<EntityRow>> Handle(ListEntitiesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListEntitiesAsync(q.WorkspaceId, ct);
    }
}
