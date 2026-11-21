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

[Access(AccessKind.Internal)]
public sealed record SetDeploymentHealthCommand(Guid DeploymentId, string State, string? Reason) : ICommand<bool>;

public sealed class SetDeploymentHealthCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetDeploymentHealthCommand, bool>
{
    public async ValueTask<bool> Handle(SetDeploymentHealthCommand c, CancellationToken ct)
    {
        await db.SetDeploymentStateAsync(c.DeploymentId, c.State, c.Reason, null, ct);
        if (c.State == "degraded") await DeploymentAlerts.SendAsync(db, clock, c.DeploymentId, "deployment.degraded", "degraded (replica not responding)", "warning", ct);
        return true;
    }
}
