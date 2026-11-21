using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Records one automatic removal (sync rows marked removed + event on the host).</summary>
public sealed record RecordDataCacheCleanupCommand(JanitorCacheRow Cache, string Reason) : ICommand<bool>, IInternalCommand;

/// <summary>Marks the cache removed and emits `data.cache_removed`.</summary>
public sealed class RecordDataCacheCleanupCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordDataCacheCleanupCommand, bool>
{
    public async ValueTask<bool> Handle(RecordDataCacheCleanupCommand c, CancellationToken ct)
    {
        var r = c.Cache;
        await db.MarkDataCacheRemovedAsync(r.WorkspaceId, r.HostId, r.DatasetId, clock.UtcNow, ct);
        await TimelineEvents.EmitAsync(db, clock, r.WorkspaceId, "system:janitor", "host", r.HostId, "data.cache_removed", "info",
            $"Dataset cache removed ({DataSyncRecorder.Size(r.CacheBytes)}): {c.Reason}",
            new JsonObject { ["dataset_id"] = r.DatasetId.ToString(), ["bytes"] = r.CacheBytes, ["reason"] = c.Reason }, ct: ct);
        return true;
    }
}
