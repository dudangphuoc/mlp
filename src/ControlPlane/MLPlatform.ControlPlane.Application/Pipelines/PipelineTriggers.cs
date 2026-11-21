using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Pipelines;

/// <summary>Schedule triggers (called from the background loop) and event triggers (M13 drift…): create a run on behalf of the pipeline creator.</summary>
public static class PipelineTriggers
{
    public static async Task<int> FireAsync(IWriteSession db, IClock clock, string eventKind, Guid workspaceId, JsonObject? eventParams, CancellationToken ct)
    {
        var n = 0;
        foreach (var p in await db.ListTriggeredAsync(clock.UtcNow, eventKind, workspaceId, ct))
        {
            var isEvent = eventKind != "" && p.Trigger.Contains("\"event\"");
            await PipelineDef.CreateRunAsync(db, clock, p, isEvent ? eventParams : null, isEvent ? "event:" + eventKind : "interval", p.CreatedBy, ct);
            await db.MarkTriggeredAsync(p.Id, clock.UtcNow, ct);
            n++;
        }
        return n;
    }
}
