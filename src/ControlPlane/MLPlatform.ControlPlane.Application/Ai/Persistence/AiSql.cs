using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

public static partial class AiSql
{
    [SqlQuery("Ai/ListConnections.sql")] public static partial Task<List<AiConnectionRow>> ListAiConnectionsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Ai/GetConnection.sql")] public static partial Task<AiConnectionRow?> GetAiConnectionAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Ai/InsertConnection.sql")]
    public static partial Task InsertAiConnectionAsync(this IWriteSession db, Guid id, string name, string providerType, string endpoint, string? secretEnc,
        int maxConcurrency, int rpmLimit, int timeoutMs, bool isExternal, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/UpdateConnection.sql")]
    public static partial Task UpdateAiConnectionAsync(this IWriteSession db, Guid id, string name, string endpoint, string? secretEnc, int maxConcurrency,
        int rpmLimit, int timeoutMs, bool isActive, bool isExternal, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/DeleteConnection.sql")] public static partial Task DeleteAiConnectionAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Ai/ListModels.sql")] public static partial Task<List<AiModelRow>> ListAiModelsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Ai/GetModel.sql")] public static partial Task<AiModelRow?> GetAiModelAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Ai/InsertModel.sql")]
    public static partial Task InsertAiModelAsync(this IWriteSession db, Guid id, Guid connectionId, string modelId, string displayName, string[] roles,
        string[] inputs, string[] outputs, string[] skills, string tier, double? inputUsdPer1m, double? outputUsdPer1m, int maxTokens, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/UpdateModel.sql")]
    public static partial Task UpdateAiModelAsync(this IWriteSession db, Guid id, string displayName, string[] roles, string[] inputs, string[] outputs,
        string[] skills, string tier, double? inputUsdPer1m, double? outputUsdPer1m, int maxTokens, bool isActive, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/DeleteModel.sql")] public static partial Task DeleteAiModelAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Ai/RecordModelOutcome.sql")]
    public static partial Task RecordAiModelOutcomeAsync(this IWriteSession db, Guid id, bool ok, string? reason, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/SetLastTest.sql")] public static partial Task SetAiLastTestAsync(this IWriteSession db, Guid id, Guid runId, CancellationToken ct);
    [SqlQuery("Ai/ListRoleDefaults.sql")] public static partial Task<List<AiRoleDefaultRow>> ListAiRoleDefaultsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Ai/SetRoleDefault.sql")]
    public static partial Task SetAiRoleDefaultAsync(this IWriteSession db, string role, Guid modelId, Guid setBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/ClearRoleDefault.sql")] public static partial Task ClearAiRoleDefaultAsync(this IWriteSession db, string role, CancellationToken ct);
    [SqlQuery("Ai/InsertRun.sql")]
    public static partial Task InsertAiRunAsync(this IWriteSession db, Guid id, Guid? workspaceId, string kind, string purpose, string? subject, string trigger,
        Guid? actorId, Guid? modelRef, string? modelLabel, string selectionJson, string inputJson, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/FinishRun.sql")]
    public static partial Task FinishAiRunAsync(this IWriteSession db, Guid id, string state, string? fallback, Guid? modelRef, string? modelLabel,
        string outputJson, string? decidedBy, double? confidence, int? latencyMs, int? tokensIn, int? tokensOut, double? costUsd, string attemptsJson,
        string? errorCode, string? error, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/ListRuns.sql")]
    public static partial Task<List<AiRunRow>> ListAiRunsAsync(this IReadSession db, Guid? workspaceId, bool includeInstance, string state, Guid? modelRef,
        int lim, CancellationToken ct);
    [SqlQuery("Ai/GetRun.sql")] public static partial Task<AiRunRow?> GetAiRunAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Ai/ModelStats.sql")] public static partial Task<List<AiModelStatsRow>> AiModelStatsAsync(this IReadSession db, DateTimeOffset since, CancellationToken ct);
    [SqlQuery("Ai/RunSummary.sql")]
    public static partial Task<AiRunSummaryRow?> AiRunSummaryAsync(this IReadSession db, Guid? workspaceId, bool includeInstance, DateTimeOffset since, CancellationToken ct);
    [SqlQuery("Ai/InsertAudit.sql")]
    public static partial Task InsertAiAuditAsync(this IWriteSession db, Guid id, Guid actorId, string action, string target, string? beforeJson, string? afterJson,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/AbandonStaleRuns.sql")] public static partial Task AbandonStaleAiRunsAsync(this IWriteSession db, DateTimeOffset before, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Ai/ListAudit.sql")] public static partial Task<List<AiAuditRow>> ListAiAuditAsync(this IReadSession db, int lim, CancellationToken ct);
}
