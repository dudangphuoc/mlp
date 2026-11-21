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

/// <summary>Start a stopped or failed deployment again, on the same or another version of its model; same machine, keys
/// and address.</summary>
[RequiresPermission("deployment.manage")]
public sealed record StartDeploymentCommand(Guid WorkspaceId, Guid DeploymentId, Guid? ModelVersionId) : ICommand<DeploymentRow>;

/// <summary>Re-submits the replica through <see cref="CreateDeploymentCommand"/> with the stored machine.</summary>
public sealed class StartDeploymentCommandHandler(IReadSession db, WorkspaceAuthorizer auth, IMediator mediator, IWorkloadControl workloads) : ICommandHandler<StartDeploymentCommand, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(StartDeploymentCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var d = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        if (d.State is not ("stopped" or "failed")) throw new PreconditionException("SERVE_RUNNING", "Stop the deployment first");
        if (d.JobId is { } previous && d.State == "failed") await workloads.StopJobAsync(previous, ct);
        return await mediator.Send(new CreateDeploymentCommand(c.WorkspaceId, d.Slug, c.ModelVersionId ?? d.ModelVersionId, d.Cpus ?? 1, d.Gpus ?? -1,
            d.ImageOverride, d.Purpose, d.IdleStopMin), ct);
    }
}
