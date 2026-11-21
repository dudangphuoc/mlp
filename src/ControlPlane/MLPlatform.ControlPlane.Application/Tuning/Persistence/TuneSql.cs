using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tuning;

public static partial class TuneSql
{
    [SqlQuery("Tune/InsertSweep.sql")]
    public static partial Task InsertSweepAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectId, string name, string recipe, string variant,
        string task, string algorithm, string space, string baseParams, string objective, string direction, int maxTrials, int parallel, int seed,
        double cpus, int gpus, Guid? datasetVersionId, string? imageOverride, string? network, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Tune/GetSweep.sql")]
    public static partial Task<SweepRow?> GetSweepAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Tune/ListSweeps.sql")]
    public static partial Task<List<SweepRow>> ListSweepsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Tune/ListRunningSweeps.sql")]
    public static partial Task<List<SweepRow>> ListRunningSweepsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Tune/FinishSweep.sql")]
    public static partial Task FinishSweepAsync(this IWriteSession db, Guid id, string state, string? reason, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Tune/SetBest.sql")]
    public static partial Task SetBestAsync(this IWriteSession db, Guid id, int? bestTrial, double? bestValue, CancellationToken ct);
    [SqlQuery("Tune/InsertTrial.sql")]
    public static partial Task InsertSweepTrialAsync(this IWriteSession db, Guid sweepId, int number, string trialParams, Guid? runId, string state, string? error,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Tune/ListTrials.sql")]
    public static partial Task<List<TrialRow>> ListTrialsAsync(this IReadSession db, Guid sweepId, CancellationToken ct);
    [SqlQuery("Tune/SyncTrials.sql")]
    public static partial Task SyncTrialsAsync(this IWriteSession db, Guid sweepId, string objective, CancellationToken ct);
}
