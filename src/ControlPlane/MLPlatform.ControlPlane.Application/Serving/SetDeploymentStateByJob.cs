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

/// <summary>Internal: replica reports endpoint / health / exit.</summary>
[Access(AccessKind.Internal)]
public sealed record SetDeploymentStateByJobCommand(Guid JobId, string State, string? Reason, string? Endpoint) : ICommand<bool>;

public sealed class SetDeploymentStateByJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetDeploymentStateByJobCommand, bool>
{
    public async ValueTask<bool> Handle(SetDeploymentStateByJobCommand c, CancellationToken ct)
    {
        if (await db.GetDeploymentByJobAsync(c.JobId, ct) is not { } d) return false;
        if (d.State == "stopped") return true;
        await db.SetDeploymentStateAsync(d.Id, c.State, c.Reason, c.Endpoint, ct);
        if (c.State == "failed" && d.State != "failed") await DeploymentAlerts.SendAsync(db, clock, d.Id, "deployment.failed", "failed", "error", ct);
        return true;
    }
}
