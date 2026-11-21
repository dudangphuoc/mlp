using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

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
