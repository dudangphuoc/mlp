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

[RequiresPermission("deployment.manage")]
public sealed record CreateEndpointKeyCommand(Guid WorkspaceId, Guid DeploymentId, string Name) : ICommand<(Guid Id, string Key, string Prefix)>;

public sealed class CreateEndpointKeyCommandHandler(IWriteSession db, IClock clock, TokenService tokens, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateEndpointKeyCommand, (Guid Id, string Key, string Prefix)>
{
    public async ValueTask<(Guid Id, string Key, string Prefix)> Handle(CreateEndpointKeyCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var key = TokenService.New("mlpd_");
        var id = Ids.New();
        await db.InsertEndpointKeyAsync(id, c.DeploymentId, string.IsNullOrWhiteSpace(c.Name) ? "default" : c.Name, key[..9], tokens.Hash(key), uid, clock.UtcNow, ct);
        return (id, key, key[..9]);
    }
}
