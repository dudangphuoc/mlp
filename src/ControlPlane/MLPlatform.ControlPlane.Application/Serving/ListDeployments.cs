using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Serving;

[RequiresPermission("run.read")]
public sealed record ListDeploymentsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<DeploymentRow>>;

public sealed class ListDeploymentsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListDeploymentsQuery, IReadOnlyList<DeploymentRow>>
{
    public async ValueTask<IReadOnlyList<DeploymentRow>> Handle(ListDeploymentsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListDeploymentsAsync(q.WorkspaceId, ct);
    }
}
