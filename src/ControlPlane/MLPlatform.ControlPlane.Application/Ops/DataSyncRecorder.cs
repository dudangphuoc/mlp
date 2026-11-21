using MLPlatform.ControlPlane.Application.Scheduling;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Keeps the shim's final sync report (progress detail sync with final: true ) — the live progress is
/// overwritten by later phases.</summary>
public static class DataSyncRecorder
{
    internal static string Size(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.00} GB" : b >= 1L << 20 ? $"{b / (double)(1L << 20):0.0} MB"
        : b >= 1024 ? $"{b / 1024.0:0} KB" : $"{b} B";

    public static async Task RecordAsync(IWriteSession db, IClock clock, WorkloadAttemptRow a, JsonObject sync, CancellationToken ct)
    {
        if (sync["final"]?.GetValue<bool>() != true || a.HostIds.Length == 0 || !Guid.TryParse(sync["dataset"]?.ToString(), out var datasetId)) return;
        long L(string k) => sync[k] is JsonValue v && v.TryGetValue<long>(out var x) ? x : 0;
        var failed = (sync["failed"] as JsonArray)?.Count ?? 0;
        await db.InsertDataSyncAsync(Ids.New(), a.WorkspaceId, a.JobId, a.Id, a.HostIds[0], datasetId, (int)L("files"), (int)L("reused_files"), L("reused_bytes"),
            (int)L("done_files"), L("done_bytes"), (int)L("uncached_files"), (int)L("retries"), sync["seconds"] is JsonValue s && s.TryGetValue<double>(out var sec) ? sec : 0,
            L("cache_files"), L("cache_bytes"), L("free_bytes"), clock.UtcNow,
            Guid.TryParse(sync["version"]?.ToString(), out var vid) ? vid : null, sync["subset"]?.GetValue<bool>() == true, ct);
        await TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "shim", "attempt", a.Id, "data.synced", failed > 0 ? "warning" : "success",
            $"Dataset: {L("files")} files · {L("reused_files")} reused ({Size(L("reused_bytes"))}) · {L("done_files")} downloaded ({Size(L("done_bytes"))}) · {sync["seconds"]} s",
            sync.DeepClone().AsObject(), ct: ct);
    }
}
