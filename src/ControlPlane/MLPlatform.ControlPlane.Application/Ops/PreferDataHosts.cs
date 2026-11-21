using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Scheduler: orders the machines that fit a job so the one already holding most of the job's dataset comes
/// first (the data does not travel again), and records the choice on the job's timeline.</summary>
[Access(AccessKind.Internal)]
public sealed record PreferDataHostsCommand(Guid WorkspaceId, Guid JobId, Guid DatasetId, IReadOnlyList<Guid> Candidates) : ICommand<IReadOnlyList<Guid>>, IInternalCommand;

/// <summary>Sorts candidate hosts by cached bytes of the dataset and emits a `data.placement` event.</summary>
public sealed class PreferDataHostsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<PreferDataHostsCommand, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(PreferDataHostsCommand c, CancellationToken ct)
    {
        var held = (await db.DatasetCacheByHostAsync(c.WorkspaceId, c.DatasetId, ct)).ToDictionary(r => r.HostId);
        var ordered = c.Candidates.Select((h, i) => (h, i)).OrderByDescending(x => held.TryGetValue(x.h, out var r) ? r.CacheBytes : -1).ThenBy(x => x.i)
            .Select(x => x.h).ToList();
        if (held.Count > 0)
        {
            var first = ordered[0];
            var hit = held.TryGetValue(first, out var r);
            await TimelineEvents.EmitAsync(db, clock, c.WorkspaceId, "scheduler", "job", c.JobId, "data.placement", hit ? "info" : "warning",
                hit ? $"Placed on a machine that already holds this dataset ({r!.CacheFiles} files, {DataSyncRecorder.Size(r.CacheBytes)})"
                    : "The machines holding this dataset cannot take the job now — the dataset is downloaded on another machine",
                new System.Text.Json.Nodes.JsonObject { ["host_id"] = first.ToString(), ["cached_bytes"] = hit ? r!.CacheBytes : 0 }, ct: ct);
        }
        return ordered;
    }
}
