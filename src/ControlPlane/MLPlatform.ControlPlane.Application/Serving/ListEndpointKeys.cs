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

/// <summary>Keys of a deployment.</summary>
public sealed record ListEndpointKeysQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<IReadOnlyList<EndpointKeyRow>>;

/// <summary>Keys of one deployment, newest first (prefix only).</summary>
public sealed class ListEndpointKeysQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEndpointKeysQuery, IReadOnlyList<EndpointKeyRow>>
{
    public async ValueTask<IReadOnlyList<EndpointKeyRow>> Handle(ListEndpointKeysQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.ListEndpointKeysAsync(q.DeploymentId, ct);
    }
}
