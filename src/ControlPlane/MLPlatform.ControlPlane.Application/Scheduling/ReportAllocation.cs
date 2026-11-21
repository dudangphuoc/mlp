using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Holder reports an allocation. Returns the allocation list once every machine has reported (so the api sends StartWorkload), otherwise null.</summary>
public sealed record ReportAllocationCommand(string Token, Guid AttemptId, Guid HostId, string[] GpuUuids, string NodeIp, bool Ok, string Message)
    : ICommand<AllocationComplete?>;

public sealed class ReportAllocationCommandHandler(IWriteSession db, IClock clock, TokenService tokens)
    : ICommandHandler<ReportAllocationCommand, AllocationComplete?>
{
    public async ValueTask<AllocationComplete?> Handle(ReportAllocationCommand c, CancellationToken ct)
    {
        var a = await db.GetAttemptByTokenAsync(tokens.Hash(c.Token), ct);
        if (a is null || a.Id != c.AttemptId) throw new UnauthenticatedException("SCHED_TOKEN_INVALID", "Invalid job token");
        if (!a.HostIds.Contains(c.HostId)) throw new ForbiddenException("SCHED_ALLOCATION_MISMATCH", "Machine does not belong to the attempt");
        if (a.State != "reserving") return null;
        if (!c.Ok)
        {
            await db.SetAttemptStateAsync(a.Id, "failed", null, c.Message, "Could not reserve resources", clock.UtcNow, ct);
            // Say what the job is waiting for and who holds it — a silent retry loop looks like a hang to the user.
            var spec = JobSpecDto.Parse(a.Spec);
            var holders = spec.Gpus > 0
                ? (await MLPlatform.ControlPlane.Application.Ops.Ops2Sql.HostAttemptJobsAsync(db, a.WorkspaceId, ct))
                    .Where(h => h.AttemptId != a.Id && h.GpuUuids.Length > 0 && a.HostIds.Contains(h.HostId)).Select(h => h.JobName).Distinct().ToList()
                : [];
            var reason = c.Message != "placement_timeout" ? "Waiting for resources: " + c.Message
                : "Waiting for " + (spec.Gpus > 0 ? "GPU memory" : "free CPU/memory") + (holders.Count > 0 ? " — in use by: " + string.Join(", ", holders) : "");
            var before = await db.GetJobAsync(a.JobId, a.WorkspaceId, ct);
            await db.RequeueJobAsync(a.JobId, reason, ct);
            if (before?.StateReason != reason)
                await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "scheduler", "attempt", a.Id, "attempt.waiting", "warning", reason, ct: ct);
            if (await MLPlatform.ControlPlane.Application.Serving.ServingSql.GetDeploymentByJobAsync(db, a.JobId, ct) is { State: not "stopped" } dep)
                await MLPlatform.ControlPlane.Application.Serving.ServingSql.SetDeploymentStateAsync(db, dep.Id, dep.State, reason, null, ct);
            if (await MLPlatform.ControlPlane.Application.Notebooks.NbSql.GetServerByJobAsync(db, a.JobId, ct) is { State: "queued" or "starting" } nb)
                await MLPlatform.ControlPlane.Application.Notebooks.NbSql.SetServerStateAsync(db, nb.Id, nb.State, reason, null, null, clock.UtcNow, ct);
            return null;
        }
        await db.UpsertAllocationAsync(a.Id, c.HostId, c.GpuUuids, c.NodeIp, clock.UtcNow, ct);
        var allocs = await db.ListAllocationsAsync(a.Id, ct);
        if (allocs.Count < a.HostIds.Length) return null;
        await db.SetAttemptStateAsync(a.Id, "launching", null, null, null, clock.UtcNow, ct);
        await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "holder", "attempt", a.Id, "attempt.launching", "info",
            "Resources reserved — sending the container run command" + (c.GpuUuids.Length > 0 ? $" (GPU {string.Join(",", c.GpuUuids)})" : ""), ct: ct);
        return new AllocationComplete(a, JobSpecDto.Parse(a.Spec), allocs);
    }
}
