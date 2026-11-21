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

/// <summary>Gateway (no user): mlpd_ key → deployment by slug; returns the replica endpoint if healthy.</summary>
public sealed record ResolveEndpointQuery(string Key, Guid WorkspaceId, string Slug, bool RequireHealthy = true) : IQuery<DeploymentRow>;

public sealed class ResolveEndpointQueryHandler(IReadSession db, TokenService tokens) : IQueryHandler<ResolveEndpointQuery, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(ResolveEndpointQuery q, CancellationToken ct)
    {
        var dep = await db.GetDeploymentBySlugAsync(q.WorkspaceId, q.Slug, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var keyDep = await db.GetKeyDeploymentAsync(tokens.Hash(q.Key), ct);
        if (keyDep != dep.Id) throw new UnauthenticatedException("SERVE_KEY_INVALID", "Invalid endpoint key");
        if (q.RequireHealthy && (dep.State != "healthy" || dep.Endpoint is null)) throw new UnavailableException("SERVE_NOT_READY", $"Deployment is {dep.State}");
        return dep;
    }
}
