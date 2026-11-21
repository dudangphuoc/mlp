using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

/// <summary>Marks it stopping and returns the job so the host stops the container + Ray job
/// (WorkloadLauncher.StopJobAsync).</summary>
public sealed record StopNotebookCommand(Guid WorkspaceId, Guid ServerId) : ICommand<ServerRow>;

public sealed class StopNotebookCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IAfterCommit afterCommit, IWorkloadControl workloads) : ICommandHandler<StopNotebookCommand, ServerRow>
{
    public async ValueTask<ServerRow> Handle(StopNotebookCommand c, CancellationToken ct)
    {
        var server = await StopAsync(c, ct);
        if (server.State is "stopping" or "stopped" && server.JobId is { } job) afterCommit.StopInBackgroundAfterCommit(workloads, job);
        return server;
    }

    private async Task<ServerRow> StopAsync(StopNotebookCommand c, CancellationToken ct)
    {
        var s = await NotebookAccess.LoadAsync(db, auth, c.WorkspaceId, c.ServerId, ct);
        if (s.State is "stopped" or "failed") return s;
        if (s.State == "stopping")
        {
            await db.SetServerStateAsync(s.Id, "stopped", "Force-stopped by user: the machine had not confirmed the stop; the stop was sent again", null, null, clock.UtcNow, ct);
            return (await db.GetServerAsync(s.Id, c.WorkspaceId, ct))!;
        }
        await db.SetServerStateAsync(s.Id, "stopping", "Stopped by user", null, null, clock.UtcNow, ct);
        return s with { State = "stopping" };
    }
}
