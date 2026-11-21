using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Monitoring;

public static partial class MonitorSql
{
    [SqlQuery("Monitor/DeleteDeploymentData.sql")]
    public static partial Task DeleteMonitorDeploymentDataAsync(this IWriteSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Monitor/InsertPrediction.sql")]
    public static partial Task InsertPredictionAsync(this IWriteSession db, Guid id, Guid deploymentId, int modelVersion, DateTimeOffset now, double latencyMs,
        string features, string prediction, CancellationToken ct);
    [SqlQuery("Monitor/SetGroundTruth.sql")]
    public static partial Task<long> SetGroundTruthAsync(this IWriteSession db, Guid id, Guid deploymentId, string groundTruth, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Monitor/RecentPredictions.sql")]
    public static partial Task<List<PredictionRow>> RecentPredictionsAsync(this IReadSession db, Guid deploymentId, int limit, CancellationToken ct);
    [SqlQuery("Monitor/CountSince.sql")]
    public static partial Task<long> CountSinceAsync(this IReadSession db, Guid deploymentId, DateTimeOffset since, CancellationToken ct);
    [SqlQuery("Monitor/UpsertConfig.sql")]
    public static partial Task UpsertConfigAsync(this IWriteSession db, Guid deploymentId, Guid workspaceId, Guid? datasetVersionId, string labelColumn,
        string? baseline, long baselineRows, double psiWarn, double psiAlert, int windowSize, int minSamples, bool enabled, Guid updatedBy,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Monitor/GetConfig.sql")]
    public static partial Task<MonitorConfigRow?> GetConfigAsync(this IReadSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Monitor/ListEnabledConfigs.sql")]
    public static partial Task<List<MonitorConfigRow>> ListEnabledConfigsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Monitor/InsertSnapshot.sql")]
    public static partial Task InsertSnapshotAsync(this IWriteSession db, Guid id, Guid deploymentId, DateTimeOffset now, int samples, string status,
        string drift, string quality, CancellationToken ct);
    [SqlQuery("Monitor/ListSnapshots.sql")]
    public static partial Task<List<SnapshotRow>> ListSnapshotsAsync(this IReadSession db, Guid deploymentId, int limit, CancellationToken ct);
    [SqlQuery("Monitor/DailyQuality.sql")]
    public static partial Task<List<DailyQualityRow>> DailyQualityAsync(this IReadSession db, Guid deploymentId, DateTimeOffset since, CancellationToken ct);
}
