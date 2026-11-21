using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Features;

public sealed class FeatureDeploymentData(IWriteSession db) : IDeploymentDataOwner
{
    public Task ForgetDeploymentAsync(Guid deploymentId, CancellationToken ct) => db.DeleteDeploymentBindingAsync(deploymentId, ct);
}
