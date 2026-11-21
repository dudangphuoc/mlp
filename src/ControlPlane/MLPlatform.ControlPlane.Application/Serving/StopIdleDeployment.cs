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

public sealed record StopIdleDeploymentCommand(Guid DeploymentId, int IdleMinutes) : ICommand<bool>, IInternalCommand;

public sealed class StopIdleDeploymentCommandHandler(IWriteSession db) : ICommandHandler<StopIdleDeploymentCommand, bool>
{
    public async ValueTask<bool> Handle(StopIdleDeploymentCommand c, CancellationToken ct)
    {
        await db.SetDeploymentStateAsync(c.DeploymentId, "stopped", $"Stopped after {c.IdleMinutes} min without test calls", null, ct);
        return true;
    }
}
