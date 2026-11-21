using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.LlmOps;

public static partial class LlmSql
{
    [SqlQuery("Llm/DeleteDeploymentData.sql")]
    public static partial Task DeleteDeploymentDataAsync(this IWriteSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Llm/InsertPrompt.sql")]
    public static partial Task InsertPromptAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, string description, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Llm/ListPrompts.sql")]
    public static partial Task<List<PromptRow>> ListPromptsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Llm/GetPromptByName.sql")]
    public static partial Task<PromptRow?> GetPromptAsync(this IReadSession db, Guid workspaceId, string key, CancellationToken ct);
    [SqlQuery("Llm/InsertPromptVersion.sql")]
    public static partial Task<int> InsertPromptVersionAsync(this IWriteSession db, Guid promptId, string system, string template, string variables, Guid? deploymentId,
        double temperature, int maxTokens, string note, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Llm/ListPromptVersions.sql")]
    public static partial Task<List<PromptVersionRow>> ListPromptVersionsAsync(this IReadSession db, Guid promptId, CancellationToken ct);
    [SqlQuery("Llm/GetSettings.sql")]
    public static partial Task<LlmSettingsRow?> GetLlmSettingsAsync(this IReadSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Llm/UpsertSettings.sql")]
    public static partial Task UpsertLlmSettingsAsync(this IWriteSession db, Guid deploymentId, Guid workspaceId, string piiMode, bool blockInjection, string blockedTerms,
        bool checkOutput, double pricePer1k, Guid updatedBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Llm/InsertUsage.sql")]
    public static partial Task InsertUsageAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid deploymentId, Guid? promptId, int? promptVersion, string source,
        DateTimeOffset now, int promptTokens, int completionTokens, double latencyMs, double cost, bool blocked, string flags, CancellationToken ct);
    [SqlQuery("Llm/UsageByDay.sql")]
    public static partial Task<List<UsageDayRow>> UsageByDayAsync(this IReadSession db, Guid workspaceId, DateTimeOffset since, CancellationToken ct);
    [SqlQuery("Llm/InsertEvalSet.sql")]
    public static partial Task InsertEvalSetAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, string cases, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Llm/ListEvalSets.sql")]
    public static partial Task<List<EvalSetRow>> ListEvalSetsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Llm/GetEvalSet.sql")]
    public static partial Task<EvalSetRow?> GetEvalSetAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Llm/InsertEvalRun.sql")]
    public static partial Task InsertEvalRunAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid setId, Guid promptId, int promptVersion, double score,
        string results, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Llm/ListEvalRuns.sql")]
    public static partial Task<List<EvalRunRow>> ListEvalRunsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
}
