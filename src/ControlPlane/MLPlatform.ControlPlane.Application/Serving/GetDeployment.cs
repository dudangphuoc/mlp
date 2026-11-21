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

public sealed record GetDeploymentQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<DeploymentRow>;

public sealed class GetDeploymentQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetDeploymentQuery, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(GetDeploymentQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
    }
}
