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

public sealed record StopDeploymentCommand(Guid WorkspaceId, Guid DeploymentId) : ICommand<DeploymentRow>;

public sealed class StopDeploymentCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IAfterCommit afterCommit, IWorkloadControl workloads) : ICommandHandler<StopDeploymentCommand, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(StopDeploymentCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var d = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        await db.SetDeploymentStateAsync(d.Id, "stopped", "Stopped by user", null, ct);
        if (d.JobId is { } job) afterCommit.StopAfterCommit(workloads, job);
        return d with { State = "stopped" };
    }
}
