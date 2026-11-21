namespace MLPlatform.ControlPlane.Application.Serving;

/// <summary>A module that keeps data about a deployment drops it when the deployment is deleted, in the same
/// transaction.</summary>
public interface IDeploymentDataOwner
{
    Task ForgetDeploymentAsync(Guid deploymentId, CancellationToken ct);
}
