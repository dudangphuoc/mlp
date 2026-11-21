using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

// ───────────── Shim → control plane ─────────────

/// <summary>Standard progress (20/10 §2.2): phase change → `progress.phase` event.</summary>
public sealed record ReportProgressCommand(string Token, string Phase, double Current, double Total, string Unit, double Percent, double Rate, double EtaSeconds,
    string Message, int StallAfterMin = 0, string DetailJson = "") : ICommand<bool>, IInternalCommand;

public sealed class ReportProgressCommandHandler(IWriteSession db, IClock clock, TokenService tokens) : ICommandHandler<ReportProgressCommand, bool>
{
    public async ValueTask<bool> Handle(ReportProgressCommand c, CancellationToken ct)
    {
        var a = await Workload.ResolveAsync(db, tokens, c.Token, ct);
        var old = await db.JobProgressAsync(a.JobId, a.WorkspaceId, ct);
        var oldPhase = old?.Progress is { } p ? JsonNode.Parse(p)?["phase"]?.ToString() : null;
        var progress = new JsonObject
        {
            ["phase"] = c.Phase, ["current"] = c.Current, ["total"] = c.Total, ["unit"] = c.Unit, ["percent"] = Math.Round(c.Percent, 2),
            ["rate"] = c.Rate, ["eta_seconds"] = Math.Round(c.EtaSeconds), ["message"] = c.Message, ["attempt_id"] = a.Id.ToString(),
            ["stall_after_min"] = c.StallAfterMin > 0 ? c.StallAfterMin : 30,
        };
        if (c.DetailJson.Length is > 0 and < 8192)
            try { if (JsonNode.Parse(c.DetailJson) is JsonObject d) progress["detail"] = d; } catch (System.Text.Json.JsonException) { }
        if (c.Phase == "preparing_data" && progress["detail"]?["sync"] is JsonObject sync) await DataSyncRecorder.RecordAsync(db, clock, a, sync, ct);
        var oldCurrent = old?.Progress is { } op && JsonNode.Parse(op)?["current"] is JsonValue ov && ov.TryGetValue<double>(out var oc) ? oc : double.NaN;
        await db.SetProgressAsync(a.JobId, progress.ToJsonString(), clock.UtcNow, ct);
        await db.AttemptHeartbeatAsync(a.Id, clock.UtcNow, ct);
        await Ops2Sql.ClearSignalLostAsync(db, a.Id, ct);
        if (oldPhase != c.Phase || oldCurrent != c.Current) await Ops2Sql.ProgressChangedAsync(db, a.JobId, clock.UtcNow, ct);
        if (oldPhase != c.Phase)
            await TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "shim", "attempt", a.Id, "progress.phase", "info",
                Phases.Label(c.Phase) + (string.IsNullOrEmpty(c.Message) ? "" : ": " + c.Message), new JsonObject { ["phase"] = c.Phase }, ct: ct);
        return true;
    }
}
