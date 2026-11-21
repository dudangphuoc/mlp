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

/// <summary>Marks a replica as used now (keeps a test replica alive).</summary>
public sealed record TouchDeploymentCommand(Guid DeploymentId) : ICommand<bool>, IInternalCommand;

public sealed class TouchDeploymentCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<TouchDeploymentCommand, bool>
{
    public async ValueTask<bool> Handle(TouchDeploymentCommand c, CancellationToken ct)
    {
        await db.TouchDeploymentAsync(c.DeploymentId, clock.UtcNow, ct);
        return true;
    }
}
