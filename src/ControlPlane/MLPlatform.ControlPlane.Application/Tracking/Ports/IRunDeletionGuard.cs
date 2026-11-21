namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A module that still needs a run (for example a model version registered from it) can refuse its
/// deletion.</summary>
public interface IRunDeletionGuard
{
    Task EnsureDeletableAsync(Guid runId, CancellationToken ct);
}
