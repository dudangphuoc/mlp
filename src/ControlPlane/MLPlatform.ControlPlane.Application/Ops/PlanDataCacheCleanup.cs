using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Picks the cache volumes to delete: idle longer than MaxIdle, plus — on a machine whose free disk is below
/// MinFreeBytes — idle caches oldest first until enough is freed.</summary>
public sealed record PlanDataCacheCleanupCommand : ICommand<IReadOnlyList<(JanitorCacheRow Cache, string Reason)>>, IInternalCommand;

/// <summary>Computes the clean-up plan from the stored sync reports.</summary>
public sealed class PlanDataCacheCleanupCommandHandler(IReadSession db, IClock clock, IMediator mediator)
    : ICommandHandler<PlanDataCacheCleanupCommand, IReadOnlyList<(JanitorCacheRow Cache, string Reason)>>
{
    public async ValueTask<IReadOnlyList<(JanitorCacheRow Cache, string Reason)>> Handle(PlanDataCacheCleanupCommand c, CancellationToken ct)
    {
        var plan = new List<(JanitorCacheRow, string)>();
        var now = clock.UtcNow;
        foreach (var host in (await db.ListAllDataCachesAsync(ct)).GroupBy(r => r.HostId))
        {
            var idle = new List<JanitorCacheRow>();
            foreach (var r in host)
                if (await mediator.Query(new MLPlatform.ControlPlane.Application.Scheduling.JobStateQuery(r.LastJobId), ct) is null or "succeeded" or "failed" or "cancelled" or "stopped")
                    idle.Add(r);
            var free = host.MaxBy(r => r.LastUsedAt)!.FreeBytes;
            foreach (var r in idle)
            {
                if (now - r.LastUsedAt > DataCacheJanitorRules.MaxIdle) { plan.Add((r, $"unused for {(int)(now - r.LastUsedAt).TotalDays} days")); free += r.CacheBytes; }
                else if (free < DataCacheJanitorRules.MinFreeBytes) { plan.Add((r, $"disk low ({DataSyncRecorder.Size(free)} free)")); free += r.CacheBytes; }
            }
        }
        return plan;
    }
}
