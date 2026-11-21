using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

public sealed record NotificationRow(Guid Id, string Kind, string Title, string Link, string ResourceUri, string Severity, int Count,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ReadAt);
public sealed record PreferenceRow(string Kind, bool Enabled);
public sealed record UserIdRow(Guid Id);
public sealed record JobInfoRow(Guid Id, Guid WorkspaceId, Guid OwnerId, string Kind, string Name, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);
public sealed record RunInfoRow(Guid Id, Guid WorkspaceId, Guid UserId, Guid ProjectId, string Name);
public sealed record DeploymentInfoRow(Guid Id, Guid WorkspaceId, Guid CreatedBy, string Slug);
public sealed record HostNotifyRow(Guid Id, Guid WorkspaceId, string Name, string State);

public static partial class NotifySql
{
    [SqlQuery("Notify/IsDisabled.sql")]
    public static partial Task<long> IsDisabledAsync(this IReadSession db, Guid userId, Guid workspaceId, string kind, CancellationToken ct);
    [SqlQuery("Notify/MergeRecent.sql")]
    public static partial Task<long> MergeRecentAsync(this IWriteSession db, Guid userId, Guid workspaceId, string kind, string resourceUri, string title,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Notify/InsertNotification.sql")]
    public static partial Task InsertNotificationAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid userId, string kind, string title, string link,
        string resourceUri, string severity, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Notify/ListNotifications.sql")]
    public static partial Task<List<NotificationRow>> ListNotificationsAsync(this IReadSession db, Guid userId, Guid workspaceId, bool unreadOnly, CancellationToken ct);
    [SqlQuery("Notify/ListChangedSince.sql")]
    public static partial Task<List<NotificationRow>> ListChangedSinceAsync(this IReadSession db, Guid userId, Guid workspaceId, DateTimeOffset since, CancellationToken ct);
    [SqlQuery("Notify/CountUnread.sql")]
    public static partial Task<long> CountUnreadAsync(this IReadSession db, Guid userId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Notify/MarkRead.sql")]
    public static partial Task MarkReadAsync(this IWriteSession db, Guid userId, Guid workspaceId, bool all, Guid[] ids, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Notify/ListPreferences.sql")]
    public static partial Task<List<PreferenceRow>> ListPreferencesAsync(this IReadSession db, Guid userId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Notify/UpsertPreference.sql")]
    public static partial Task UpsertPreferenceAsync(this IWriteSession db, Guid userId, Guid workspaceId, string kind, bool enabled, CancellationToken ct);
    [SqlQuery("Notify/WorkspaceAdmins.sql")]
    public static partial Task<List<UserIdRow>> WorkspaceAdminsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Notify/JobInfo.sql")]
    public static partial Task<JobInfoRow?> JobInfoAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Notify/RunInfoByJob.sql")]
    public static partial Task<RunInfoRow?> RunInfoByJobAsync(this IReadSession db, Guid jobId, CancellationToken ct);
    [SqlQuery("Notify/DeploymentInfo.sql")]
    public static partial Task<DeploymentInfoRow?> DeploymentInfoAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Notify/HostInfoForNotify.sql")]
    public static partial Task<HostNotifyRow?> HostInfoForNotifyAsync(this IReadSession db, Guid id, CancellationToken ct);
}

/// <summary>P1 notification kind catalog (M16 §4) — groups shown in S-16-2; `Mandatory` cannot be turned off (R3).</summary>
public static class NotificationKinds
{
    public sealed record Kind(string Key, string Group, string Label, bool Mandatory = false);

    public static readonly Kind[] All =
    [
        new("job.failed", "Job", "Job failed"),
        new("job.succeeded", "Job", "Job finished (ran > 10 minutes)"),
        new("run.finished", "Job", "Training run finished"),
        new("run.failed", "Job", "Training run failed"),
        new("notebook.idle_warning", "Job", "Notebook about to stop automatically"),
        new("job.signal_lost", "Job", "Job lost signal"),
        new("job.stalled", "Job", "Job may be stalled"),
        new("sweep.completed", "Job", "HPO sweep finished"),
        new("sweep.failed", "Job", "HPO sweep failed"),
        new("pipeline.succeeded", "Job", "Pipeline run finished"),
        new("pipeline.failed", "Job", "Pipeline failed"),
        new("labeling.assigned", "Labeling", "Item assigned"),
        new("labeling.rejected", "Labeling", "Item rejected"),
        new("labeling.project_completed", "Labeling", "Labeling project completed"),
        new("model.promoted", "Model", "Model promoted to production"),
        new("model.evaluated", "Model", "Model evaluation finished"),
        new("deployment.degraded", "Model", "Deployment degraded"),
        new("deployment.failed", "Model", "Deployment failed"),
        new("deployment.drift", "Model", "Deployment data drift"),
        new("fleet.host_offline", "Machines", "Machine offline"),
        new("audit.integrity_broken", "System", "Audit chain broken", Mandatory: true),
    ];

    public static Kind? Find(string key) => All.FirstOrDefault(k => k.Key == key);
}

/// <summary>
/// Writes notifications in the same transaction as the source event (M16 §5; replaces the outbox for P1): skips users who turned that kind off (R3),
/// merges the same kind on the same resource within 5 minutes (R1). Links are relative web hash routes (R2: no sensitive data).
/// </summary>
public static class Notifier
{
    public static async Task SendAsync(IWriteSession db, IClock clock, Guid workspaceId, IEnumerable<Guid> users, string kind, string title,
        string link, string resourceUri, string severity, CancellationToken ct)
    {
        var mandatory = NotificationKinds.Find(kind)?.Mandatory ?? false;
        var now = clock.UtcNow;
        foreach (var uid in users.Distinct())
        {
            if (!mandatory && await db.IsDisabledAsync(uid, workspaceId, kind, ct) > 0) continue;
            if (await db.MergeRecentAsync(uid, workspaceId, kind, resourceUri, title, now, ct) > 0) continue;
            await db.InsertNotificationAsync(Ids.New(), workspaceId, uid, kind, title, link, resourceUri, severity, now, ct);
        }
    }

    public static async Task<IEnumerable<Guid>> AdminsAsync(IReadSession db, Guid workspaceId, CancellationToken ct) =>
        (await db.WorkspaceAdminsAsync(workspaceId, ct)).Select(r => r.Id);

    /// <summary>Job finished: train → run.finished/failed (to the run creator); other jobs → job.failed, job.succeeded only when it ran > 10 minutes.</summary>
    public static async Task JobFinishedAsync(IWriteSession db, IClock clock, Guid jobId, string jobState, string? reason, CancellationToken ct)
    {
        if (jobState is not ("succeeded" or "failed")) return;
        if (await db.RunInfoByJobAsync(jobId, ct) is { } run)
        {
            var ok = jobState == "succeeded";
            await SendAsync(db, clock, run.WorkspaceId, [run.UserId], ok ? "run.finished" : "run.failed",
                ok ? $"Run \"{run.Name}\" finished" : $"Run \"{run.Name}\" failed", $"#/projects/{run.ProjectId}/runs/{run.Id}",
                $"run/{run.Id}", ok ? "success" : "error", ct);
            return;
        }
        if (await db.JobInfoAsync(jobId, ct) is not { } job) return;
        if (jobState == "failed")
            await SendAsync(db, clock, job.WorkspaceId, [job.OwnerId], "job.failed", $"Job \"{job.Name}\" failed{(reason is null ? "" : $" ({reason})")}",
                "#/jobs", $"job/{job.Id}", "error", ct);
        else if (job.StartedAt is { } s && clock.UtcNow - s > TimeSpan.FromMinutes(10))
            await SendAsync(db, clock, job.WorkspaceId, [job.OwnerId], "job.succeeded", $"Job \"{job.Name}\" finished", "#/jobs", $"job/{job.Id}", "success", ct);
    }
}

// ───────────── RPC ─────────────

public sealed record ListNotificationsQuery(Guid WorkspaceId, bool UnreadOnly) : IQuery<(IReadOnlyList<NotificationRow> Items, long Unread)>;

public sealed class ListNotificationsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListNotificationsQuery, (IReadOnlyList<NotificationRow> Items, long Unread)>
{
    public async ValueTask<(IReadOnlyList<NotificationRow> Items, long Unread)> Handle(ListNotificationsQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return (await db.ListNotificationsAsync(uid, q.WorkspaceId, q.UnreadOnly, ct), await db.CountUnreadAsync(uid, q.WorkspaceId, ct));
    }
}

/// <summary>For WatchNotifications: records changed after `since` + unread count. Polled every second (acceptance ≤ 2 s).</summary>
public sealed record NotificationsChangedQuery(Guid WorkspaceId, DateTimeOffset Since) : IQuery<(IReadOnlyList<NotificationRow> Items, long Unread)>;

public sealed class NotificationsChangedQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<NotificationsChangedQuery, (IReadOnlyList<NotificationRow> Items, long Unread)>
{
    public async ValueTask<(IReadOnlyList<NotificationRow> Items, long Unread)> Handle(NotificationsChangedQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return (await db.ListChangedSinceAsync(uid, q.WorkspaceId, q.Since, ct), await db.CountUnreadAsync(uid, q.WorkspaceId, ct));
    }
}

/// <summary>Marks as read (a list of ids, or all). Content is not audited — it only affects the user's own data.</summary>
public sealed record MarkNotificationsReadCommand(Guid WorkspaceId, IReadOnlyList<Guid> Ids, bool All) : ICommand<long>;

public sealed class MarkNotificationsReadCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<MarkNotificationsReadCommand, long>
{
    public async ValueTask<long> Handle(MarkNotificationsReadCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.read", ct);
        await db.MarkReadAsync(uid, c.WorkspaceId, c.All, c.Ids.ToArray(), clock.UtcNow, ct);
        return await db.CountUnreadAsync(uid, c.WorkspaceId, ct);
    }
}

public sealed record PreferenceView(string Kind, string Group, string Label, bool Mandatory, bool Enabled);

public sealed record GetNotificationPreferencesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<PreferenceView>>;

public sealed class GetNotificationPreferencesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetNotificationPreferencesQuery, IReadOnlyList<PreferenceView>>
{
    public async ValueTask<IReadOnlyList<PreferenceView>> Handle(GetNotificationPreferencesQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        var off = (await db.ListPreferencesAsync(uid, q.WorkspaceId, ct)).Where(p => !p.Enabled).Select(p => p.Kind).ToHashSet();
        return NotificationKinds.All.Select(k => new PreferenceView(k.Key, k.Group, k.Label, k.Mandatory, k.Mandatory || !off.Contains(k.Key))).ToList();
    }
}

public sealed record UpdateNotificationPreferencesCommand(Guid WorkspaceId, IReadOnlyList<(string Kind, bool Enabled)> Changes) : ICommand<bool>;

public sealed class UpdateNotificationPreferencesCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<UpdateNotificationPreferencesCommand, bool>
{
    public async ValueTask<bool> Handle(UpdateNotificationPreferencesCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.read", ct);
        foreach (var (kind, enabled) in c.Changes)
        {
            var k = NotificationKinds.Find(kind) ?? throw new ValidationException(new Dictionary<string, string> { ["kind"] = $"Unknown kind {kind}" });
            if (k.Mandatory && !enabled) throw new InvalidArgumentException("NOTIFY_KIND_MANDATORY", "This notification kind cannot be turned off");
            await db.UpsertPreferenceAsync(uid, c.WorkspaceId, kind, enabled, ct);
        }
        return true;
    }
}
