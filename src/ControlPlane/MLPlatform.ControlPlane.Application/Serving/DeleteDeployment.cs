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

/// <summary>Delete a stopped deployment with its keys and monitoring history; the name becomes free.</summary>
public sealed record DeleteDeploymentCommand(Guid WorkspaceId, Guid DeploymentId) : ICommand<bool>;

/// <summary>Refuses while running; otherwise removes the deployment's rows in every module.</summary>
public sealed class DeleteDeploymentCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IAfterCommit afterCommit, IWorkloadControl workloads) : ICommandHandler<DeleteDeploymentCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteDeploymentCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var d = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        if (d.State is not ("stopped" or "failed")) throw new PreconditionException("SERVE_RUNNING", "Stop the deployment first");
        // Each module drops what it keeps about the deployment; one write session = one transaction.
        await MLPlatform.ControlPlane.Application.Monitoring.MonitorSql.DeleteMonitorDeploymentDataAsync(db, d.Id, ct);
        await MLPlatform.ControlPlane.Application.LlmOps.LlmSql.DeleteDeploymentDataAsync(db, d.Id, ct);
        await MLPlatform.ControlPlane.Application.Features.FeatureSql.DeleteDeploymentBindingAsync(db, d.Id, ct);
        await MLPlatform.ControlPlane.Application.Registry.RegistrySql.ForgetDeploymentAsync(db, d.Id, ct);
        await db.DeleteDeploymentAsync(d.Id, ct);
        if (d.JobId is { } job && d.State == "failed") afterCommit.StopAfterCommit(workloads, job);
        return true;
    }
}
