using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Queues job stops after the commit; a stop must outlive the request, so it never uses the request's token.</summary>
public static class WorkloadStops
{
    public static void StopAfterCommit(this IAfterCommit queue, IWorkloadControl workloads, Guid jobId) =>
        queue.Enqueue(Key(jobId), _ => workloads.StopJobAsync(jobId, CancellationToken.None));

    public static void StopAfterCommit(this IAfterCommit queue, IWorkloadControl workloads, IEnumerable<Guid> jobIds)
    {
        foreach (var job in jobIds) queue.StopAfterCommit(workloads, job);
    }

    public static void StopAfterCommitTolerant(this IAfterCommit queue, IWorkloadControl workloads, Guid jobId) =>
        queue.Enqueue(Key(jobId), _ => StopIgnoringUnreachableMachineAsync(workloads, jobId));

    public static void StopInBackgroundAfterCommit(this IAfterCommit queue, IWorkloadControl workloads, Guid jobId) =>
        queue.Enqueue(Key(jobId), ct =>
        {
            _ = Task.Run(() => StopIgnoringUnreachableMachineAsync(workloads, jobId));
            return Task.CompletedTask;
        });

    private static async Task StopIgnoringUnreachableMachineAsync(IWorkloadControl workloads, Guid jobId)
    {
        try { await workloads.StopJobAsync(jobId, CancellationToken.None); }
        catch (Exception e) when (e is TimeoutException or InvalidOperationException or IOException or ObjectDisposedException) { }
    }

    private static string Key(Guid jobId) => $"stop:{jobId}";
}
