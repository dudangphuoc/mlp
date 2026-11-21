namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Stops a job's containers and frees its reservation on the machines (implemented by the workload
/// launcher).</summary>
public interface IWorkloadControl
{
    Task StopJobAsync(Guid jobId, CancellationToken ct);
}
