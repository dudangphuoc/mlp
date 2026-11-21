namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Modules that own a job (deployments, notebooks) show why it waits; called in the scheduler's transaction.</summary>
public interface IJobWaitObserver
{
    Task OnJobWaitingAsync(Guid jobId, string reason, CancellationToken ct);
}
