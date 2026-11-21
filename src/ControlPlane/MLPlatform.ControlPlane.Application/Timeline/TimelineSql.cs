using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Timeline;

/// <summary>Job and attempt timeline events (sched.events), shared by every module that reports progress.</summary>
public static partial class TimelineSql
{
    [SqlQuery("Ops/InsertEvent.sql")]
    public static partial Task InsertTimelineEventAsync(this IWriteSession db, Guid id, DateTimeOffset at, DateTimeOffset now, Guid? workspaceId, string source, string resourceType,
        Guid? resourceId, string kind, string severity, string message, string data, CancellationToken ct);
    [SqlQuery("Ops/ListEvents.sql")]
    public static partial Task<List<EventRow>> ListTimelineEventsAsync(this IReadSession db, Guid jobId, CancellationToken ct);
    [SqlQuery("Ops/JobWorkspace.sql")]
    public static partial Task<Guid?> JobWorkspaceAsync(this IReadSession db, Guid id, CancellationToken ct);
}
