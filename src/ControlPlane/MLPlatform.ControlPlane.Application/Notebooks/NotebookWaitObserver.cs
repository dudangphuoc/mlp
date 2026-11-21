using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public sealed class NotebookWaitObserver(IWriteSession db, IClock clock) : IJobWaitObserver
{
    public async Task OnJobWaitingAsync(Guid jobId, string reason, CancellationToken ct)
    {
        if (await db.GetServerByJobAsync(jobId, ct) is { State: "queued" or "starting" } server)
            await db.SetServerStateAsync(server.Id, server.State, reason, null, null, clock.UtcNow, ct);
    }
}
