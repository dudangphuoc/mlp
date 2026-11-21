using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Serving;

public sealed class DeploymentWaitObserver(IWriteSession db) : IJobWaitObserver
{
    public async Task OnJobWaitingAsync(Guid jobId, string reason, CancellationToken ct)
    {
        if (await db.GetDeploymentByJobAsync(jobId, ct) is { State: not "stopped" } deployment)
            await db.SetDeploymentStateAsync(deployment.Id, deployment.State, reason, null, ct);
    }
}
