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

/// <summary>Replica the playground may call: any deployment the user can read, as long as it is healthy.</summary>
public sealed record ResolveTestTargetQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<DeploymentRow>;

public sealed class ResolveTestTargetQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ResolveTestTargetQuery, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(ResolveTestTargetQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var d = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        if (d.State != "healthy" || d.Endpoint is null)
            throw new UnavailableException("SERVE_NOT_READY", $"The replica is not ready yet ({d.State})");
        return d;
    }
}
