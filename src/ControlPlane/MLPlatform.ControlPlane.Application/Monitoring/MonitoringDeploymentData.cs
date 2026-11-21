using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Monitoring;

public sealed class MonitoringDeploymentData(IWriteSession db) : IDeploymentDataOwner
{
    public Task ForgetDeploymentAsync(Guid deploymentId, CancellationToken ct) => db.DeleteMonitorDeploymentDataAsync(deploymentId, ct);
}
