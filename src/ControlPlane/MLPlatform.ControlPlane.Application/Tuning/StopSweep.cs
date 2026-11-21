using MLPlatform.ControlPlane.Application.Scheduling;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tuning;

/// <summary>Stop a sweep: no new trials are generated; returns the running trials' jobs so the host stops the
/// containers (WorkloadLauncher.StopJobAsync).</summary>
public sealed record StopSweepCommand(Guid WorkspaceId, Guid SweepId) : ICommand<(SweepRow Sweep, IReadOnlyList<Guid> ActiveJobs)>;

public sealed class StopSweepCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IAfterCommit afterCommit, IWorkloadControl workloads) : ICommandHandler<StopSweepCommand, (SweepRow Sweep, IReadOnlyList<Guid> ActiveJobs)>
{
    public async ValueTask<(SweepRow Sweep, IReadOnlyList<Guid> ActiveJobs)> Handle(StopSweepCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var s = await db.GetSweepAsync(c.SweepId, c.WorkspaceId, ct) ?? throw new NotFoundException("TUNE_SWEEP_NOT_FOUND", "Sweep not found");
        if (s.State != "running") return (s, []);
        await db.FinishSweepAsync(s.Id, "stopped", "Stopped by user", clock.UtcNow, ct);
        var active = (await db.ListTrialsAsync(s.Id, ct)).Where(t => t.State is "queued" or "running" && t.JobId is not null).Select(t => t.JobId!.Value).ToList();
        afterCommit.StopAfterCommit(workloads, active);
        return ((await db.GetSweepAsync(s.Id, c.WorkspaceId, ct))!, active);
    }
}
