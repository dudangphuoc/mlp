using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public static partial class HermesSql
{
    [SqlQuery("Label/InsertHermesCheck.sql")]
    public static partial Task InsertHermesCheckAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectId, string path, int revision, string trigger,
        string verdict, double? confidence, string decidedBy, string summary, string stepsJson, string? suggestion, Guid? aiRunId, string action, Guid? actorId,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/GetHermesCheck.sql")]
    public static partial Task<HermesCheckRow?> GetHermesCheckAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/ListHermesChecks.sql")]
    public static partial Task<List<HermesCheckRow>> ListHermesChecksAsync(this IReadSession db, Guid workspaceId, Guid projectId, string verdict, string path,
        Guid? onlyAnnotator, int lim, CancellationToken ct);
    [SqlQuery("Label/HermesStats.sql")]
    public static partial Task<List<HermesStatRow>> HermesStatsAsync(this IReadSession db, Guid workspaceId, Guid projectId, CancellationToken ct);
    [SqlQuery("Label/MarkHermesUndone.sql")]
    public static partial Task MarkHermesUndoneAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid undoneBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/SetHermesSettings.sql")]
    public static partial Task SetHermesSettingsAsync(this IWriteSession db, Guid id, Guid workspaceId, bool enabled, double threshold, CancellationToken ct);
}
