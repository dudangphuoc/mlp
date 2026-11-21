using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.LlmOps;

public sealed class LlmOpsDeploymentData(IWriteSession db) : IDeploymentDataOwner
{
    public Task ForgetDeploymentAsync(Guid deploymentId, CancellationToken ct) => db.DeleteDeploymentDataAsync(deploymentId, ct);
}
