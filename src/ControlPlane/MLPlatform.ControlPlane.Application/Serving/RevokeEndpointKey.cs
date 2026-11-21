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

/// <summary>Revoke one endpoint key.</summary>
[RequiresPermission("deployment.manage")]
public sealed record RevokeEndpointKeyCommand(Guid WorkspaceId, Guid DeploymentId, Guid KeyId) : ICommand<bool>;

/// <summary>Marks the key revoked; the gateway stops accepting it at once.</summary>
public sealed class RevokeEndpointKeyCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<RevokeEndpointKeyCommand, bool>
{
    public async ValueTask<bool> Handle(RevokeEndpointKeyCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        await db.RevokeEndpointKeyAsync(c.KeyId, c.DeploymentId, clock.UtcNow, ct);
        return true;
    }
}
