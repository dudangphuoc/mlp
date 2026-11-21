using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

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
