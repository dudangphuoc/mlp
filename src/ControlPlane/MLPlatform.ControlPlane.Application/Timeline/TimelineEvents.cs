using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Timeline;

/// <summary>Appends an event to a job or attempt timeline.</summary>
public static class TimelineEvents
{
    public static Task EmitAsync(IWriteSession db, IClock clock, Guid? workspaceId, string source, string resourceType, Guid? resourceId, string kind,
        string severity, string message, JsonObject? data = null, DateTimeOffset? at = null, CancellationToken ct = default) =>
        db.InsertTimelineEventAsync(Ids.New(), at ?? clock.UtcNow, clock.UtcNow, workspaceId, source, resourceType, resourceId, kind, severity, message,
            (data ?? []).ToJsonString(), ct);
}
