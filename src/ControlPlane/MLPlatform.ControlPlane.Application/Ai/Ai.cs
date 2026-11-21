using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/*
 * AI providers (Hermes phase 1). Connections (endpoint + encrypted key + limits) hold declared models (capabilities, roles,
 * tier, price). One explicit default model per role. Every AI call is an ai.runs row so the UI can show it — nothing runs unseen.
 * Instance-wide; managed by the Instance Admin. Calling providers and encrypting keys happens in the api (AiRuntime).
 */

public sealed record AiConnectionRow(Guid Id, string Name, string ProviderType, string Endpoint, string? SecretEnc, int MaxConcurrency, int RpmLimit,
    int TimeoutMs, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsExternal);

public sealed record AiModelRow(Guid Id, Guid ConnectionId, string ModelId, string DisplayName, string[] Roles, string[] Inputs, string[] Outputs,
    string[] Skills, string Tier, double? InputUsdPer1m, double? OutputUsdPer1m, int MaxTokens, bool IsActive, string Health, string? HealthReason,
    DateTimeOffset? HealthAt, int ConsecutiveFailures, Guid? LastTestRunId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record AiRoleDefaultRow(string Role, Guid ModelId);

public sealed record AiRunRow(Guid Id, Guid? WorkspaceId, string Kind, string Purpose, string? Subject, string Trigger, Guid? ActorId, string? ActorName, string State,
    string? Fallback, Guid? ModelRef, string? ModelLabel, string SelectionJson, string InputJson, string OutputJson, string? DecidedBy, double? Confidence,
    int? LatencyMs, int? TokensIn, int? TokensOut, double? CostUsd, string AttemptsJson, string? ErrorCode, string? Error, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record AiModelStatsRow(Guid ModelId, long Runs, long Failures, long Fallbacks, int P50Ms, int P95Ms);
public sealed record AiRunSummaryRow(long Running, long FailedRecent, long FallbackRecent, DateTimeOffset? LastAt);
public sealed record AiAuditRow(Guid Id, Guid ActorId, string ActorName, string Action, string Target, string? BeforeJson, string? AfterJson, DateTimeOffset At);

/// <summary>Allowed values (validated on write; the UI lists them).</summary>
public static class AiCatalog
{
    public static readonly string[] ProviderTypes = ["openai-compatible", "openai", "anthropic", "deepseek", "systemone", "typesafe"];
    public static readonly string[] Roles = ["analyst", "decision", "judge", "vision", "labeler", "embed", "draft"];
    public static readonly string[] Inputs = ["text", "image", "audio", "video", "pdf"];
    public static readonly string[] Outputs = ["text", "json", "embedding", "boxes", "masks"];
    public static readonly string[] Skills = ["streaming", "tools", "structured", "reasoning"];
    public static readonly string[] Tiers = ["small", "standard", "frontier"];

    /// <summary>Decision servers (System One wire format) — only decision/judge roles make sense there.</summary>
    public static bool IsDecisionServer(string providerType) => providerType is "systemone" or "typesafe";

    /// <summary>Hosted APIs outside the platform: data sent to them leaves the platform. Default for new connections; the admin can change it.</summary>
    public static bool ExternalByDefault(string providerType) => providerType is "openai" or "anthropic" or "deepseek" or "typesafe";

    public static string[] Clean(IEnumerable<string> values, string[] allowed, string field)
    {
        var list = values.Select(v => v.Trim().ToLowerInvariant()).Where(v => v.Length > 0).Distinct().ToArray();
        var bad = list.Where(v => !allowed.Contains(v)).ToArray();
        if (bad.Length > 0) throw new ValidationException(new Dictionary<string, string> { [field] = $"Unknown value(s): {string.Join(", ", bad)}" });
        return list;
    }
}

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

/// <summary>Everything the models page needs in one read (Instance Admin).</summary>
public sealed record AiOverview(List<AiConnectionRow> Connections, List<AiModelRow> Models, List<AiRoleDefaultRow> Defaults, List<AiModelStatsRow> Stats);
public sealed record GetAiOverviewQuery : IQuery<AiOverview>;
public sealed class GetAiOverviewQueryHandler(IReadSession db, InstanceAdminGuard guard, IClock clock) : IQueryHandler<GetAiOverviewQuery, AiOverview>
{
    public async ValueTask<AiOverview> Handle(GetAiOverviewQuery q, CancellationToken ct)
    {
        await guard.RequireAsync(ct);
        return new AiOverview(await db.ListAiConnectionsAsync(ct), await db.ListAiModelsAsync(ct), await db.ListAiRoleDefaultsAsync(ct),
            await db.AiModelStatsAsync(clock.UtcNow.AddHours(-24), ct));
    }
}

/// <summary>Catalog for the runtime (internal; no permission check — used while serving a request that was already authorized).</summary>
public sealed record GetAiCatalogInternalQuery : IQuery<AiOverview>;
public sealed class GetAiCatalogInternalQueryHandler(IReadSession db, IClock clock) : IQueryHandler<GetAiCatalogInternalQuery, AiOverview>
{
    public async ValueTask<AiOverview> Handle(GetAiCatalogInternalQuery q, CancellationToken ct) =>
        new(await db.ListAiConnectionsAsync(ct), await db.ListAiModelsAsync(ct), await db.ListAiRoleDefaultsAsync(ct), await db.AiModelStatsAsync(clock.UtcNow.AddHours(-24), ct));
}

internal static class AiAudit
{
    public static Task WriteAsync(IWriteSession db, IClock clock, Guid actor, string action, string target, object? before, object? after, CancellationToken ct) =>
        db.InsertAiAuditAsync(Ids.New(), actor, action, target, Json(before), Json(after), clock.UtcNow, ct);

    private static string? Json(object? o) => o switch
    {
        null => null,
        JsonNode n => n.ToJsonString(),
        string s => new JsonObject { ["value"] = s }.ToJsonString(),
        _ => new JsonObject { ["value"] = o.ToString() }.ToJsonString(),
    };

    public static JsonObject Of(AiConnectionRow c) => new()
    {
        ["name"] = c.Name, ["type"] = c.ProviderType, ["endpoint"] = c.Endpoint, ["key"] = c.SecretEnc is null ? "none" : "set",
        ["max_concurrency"] = c.MaxConcurrency, ["rpm_limit"] = c.RpmLimit, ["timeout_ms"] = c.TimeoutMs, ["active"] = c.IsActive, ["external"] = c.IsExternal,
    };

    public static JsonObject Of(AiModelRow m) => new()
    {
        ["model"] = m.ModelId, ["roles"] = string.Join(",", m.Roles), ["inputs"] = string.Join(",", m.Inputs), ["outputs"] = string.Join(",", m.Outputs),
        ["skills"] = string.Join(",", m.Skills), ["tier"] = m.Tier, ["max_tokens"] = m.MaxTokens, ["active"] = m.IsActive,
        ["price_in"] = m.InputUsdPer1m, ["price_out"] = m.OutputUsdPer1m,
    };
}

/// <summary>Create a connection (the api encrypted the key).</summary>
public sealed record CreateAiConnectionCommand(string Name, string ProviderType, string Endpoint, string? SecretEnc, int MaxConcurrency, int RpmLimit, int TimeoutMs,
    bool? IsExternal = null)
    : ICommand<AiConnectionRow>;
public sealed class CreateAiConnectionCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<CreateAiConnectionCommand, AiConnectionRow>
{
    public async ValueTask<AiConnectionRow> Handle(CreateAiConnectionCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        if (string.IsNullOrWhiteSpace(c.Name)) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Name is required" });
        if (!AiCatalog.ProviderTypes.Contains(c.ProviderType)) throw new ValidationException(new Dictionary<string, string> { ["provider_type"] = "Unknown provider type" });
        if (!Uri.TryCreate(c.Endpoint.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            throw new ValidationException(new Dictionary<string, string> { ["endpoint"] = "Enter an http(s) URL" });
        var id = Ids.New();
        try
        {
            await db.InsertAiConnectionAsync(id, c.Name.Trim(), c.ProviderType, c.Endpoint.Trim().TrimEnd('/'), c.SecretEnc,
                Math.Clamp(c.MaxConcurrency, 1, 256), Math.Max(0, c.RpmLimit), Math.Clamp(c.TimeoutMs, 500, 600_000),
                c.IsExternal ?? AiCatalog.ExternalByDefault(c.ProviderType), uid, clock.UtcNow, ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("AI_CONNECTION_EXISTS", "A connection with this name already exists"); }
        var row = (await db.GetAiConnectionAsync(id, ct))!;
        await AiAudit.WriteAsync(db, clock, uid, "connection.create", row.Name, null, AiAudit.Of(row), ct);
        return row;
    }
}

/// <summary>Update a connection. SecretEnc null = keep the stored key; ClearKey removes it.</summary>
public sealed record UpdateAiConnectionCommand(Guid Id, string Name, string Endpoint, string? SecretEnc, bool ClearKey, int MaxConcurrency, int RpmLimit,
    int TimeoutMs, bool IsActive, bool IsExternal) : ICommand<AiConnectionRow>;
public sealed class UpdateAiConnectionCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<UpdateAiConnectionCommand, AiConnectionRow>
{
    public async ValueTask<AiConnectionRow> Handle(UpdateAiConnectionCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var old = await db.GetAiConnectionAsync(c.Id, ct) ?? throw new NotFoundException("AI_CONNECTION_NOT_FOUND", "Connection not found");
        if (!Uri.TryCreate(c.Endpoint.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            throw new ValidationException(new Dictionary<string, string> { ["endpoint"] = "Enter an http(s) URL" });
        var secret = c.ClearKey ? null : c.SecretEnc ?? old.SecretEnc;
        await db.UpdateAiConnectionAsync(c.Id, string.IsNullOrWhiteSpace(c.Name) ? old.Name : c.Name.Trim(), c.Endpoint.Trim().TrimEnd('/'), secret,
            Math.Clamp(c.MaxConcurrency, 1, 256), Math.Max(0, c.RpmLimit), Math.Clamp(c.TimeoutMs, 500, 600_000), c.IsActive, c.IsExternal, clock.UtcNow, ct);
        var row = (await db.GetAiConnectionAsync(c.Id, ct))!;
        await AiAudit.WriteAsync(db, clock, uid, "connection.update", row.Name, AiAudit.Of(old), AiAudit.Of(row), ct);
        return row;
    }
}

public sealed record DeleteAiConnectionCommand(Guid Id) : ICommand<bool>;
public sealed class DeleteAiConnectionCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<DeleteAiConnectionCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteAiConnectionCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var old = await db.GetAiConnectionAsync(c.Id, ct) ?? throw new NotFoundException("AI_CONNECTION_NOT_FOUND", "Connection not found");
        await db.DeleteAiConnectionAsync(c.Id, ct);
        await AiAudit.WriteAsync(db, clock, uid, "connection.delete", old.Name, AiAudit.Of(old), null, ct);
        return true;
    }
}

/// <summary>Declare (create) or update a model on a connection.</summary>
public sealed record SaveAiModelCommand(Guid? Id, Guid ConnectionId, string ModelId, string DisplayName, string[] Roles, string[] Inputs, string[] Outputs,
    string[] Skills, string Tier, double? InputUsdPer1m, double? OutputUsdPer1m, int MaxTokens, bool IsActive) : ICommand<AiModelRow>;
public sealed class SaveAiModelCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<SaveAiModelCommand, AiModelRow>
{
    public async ValueTask<AiModelRow> Handle(SaveAiModelCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var conn = await db.GetAiConnectionAsync(c.ConnectionId, ct) ?? throw new NotFoundException("AI_CONNECTION_NOT_FOUND", "Connection not found");
        var roles = AiCatalog.Clean(c.Roles, AiCatalog.Roles, "roles");
        if (roles.Length == 0) throw new ValidationException(new Dictionary<string, string> { ["roles"] = "Pick at least one role" });
        if (AiCatalog.IsDecisionServer(conn.ProviderType) && roles.Any(r => r is not ("decision" or "judge")))
            throw new ValidationException(new Dictionary<string, string> { ["roles"] = "A decision server can only serve the decision and judge roles" });
        var inputs = AiCatalog.Clean(c.Inputs.Length == 0 ? ["text"] : c.Inputs, AiCatalog.Inputs, "inputs");
        var outputs = AiCatalog.Clean(c.Outputs.Length == 0 ? ["text"] : c.Outputs, AiCatalog.Outputs, "outputs");
        var skills = AiCatalog.Clean(c.Skills, AiCatalog.Skills, "skills");
        var tier = AiCatalog.Clean([string.IsNullOrWhiteSpace(c.Tier) ? "standard" : c.Tier], AiCatalog.Tiers, "tier")[0];
        if (string.IsNullOrWhiteSpace(c.ModelId)) throw new ValidationException(new Dictionary<string, string> { ["model_id"] = "Model name is required" });
        var maxTokens = Math.Clamp(c.MaxTokens <= 0 ? 4096 : c.MaxTokens, 16, 2_000_000);
        Guid id;
        AiModelRow? before = null;
        if (c.Id is { } existing)
        {
            before = await db.GetAiModelAsync(existing, ct) ?? throw new NotFoundException("AI_MODEL_NOT_FOUND", "Model not found");
            id = existing;
            await db.UpdateAiModelAsync(id, c.DisplayName.Trim(), roles, inputs, outputs, skills, tier, c.InputUsdPer1m, c.OutputUsdPer1m, maxTokens, c.IsActive, clock.UtcNow, ct);
        }
        else
        {
            id = Ids.New();
            try
            {
                await db.InsertAiModelAsync(id, conn.Id, c.ModelId.Trim(), c.DisplayName.Trim(), roles, inputs, outputs, skills, tier, c.InputUsdPer1m, c.OutputUsdPer1m,
                    maxTokens, clock.UtcNow, ct);
            }
            catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("AI_MODEL_EXISTS", "This model is already declared on the connection"); }
        }
        var row = (await db.GetAiModelAsync(id, ct))!;
        await AiAudit.WriteAsync(db, clock, uid, before is null ? "model.create" : "model.update", $"{conn.Name} / {row.ModelId}",
            before is null ? null : AiAudit.Of(before), AiAudit.Of(row), ct);
        return row;
    }
}

public sealed record DeleteAiModelCommand(Guid Id) : ICommand<bool>;
public sealed class DeleteAiModelCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<DeleteAiModelCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteAiModelCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var old = await db.GetAiModelAsync(c.Id, ct) ?? throw new NotFoundException("AI_MODEL_NOT_FOUND", "Model not found");
        await db.DeleteAiModelAsync(c.Id, ct);
        await AiAudit.WriteAsync(db, clock, uid, "model.delete", old.ModelId, AiAudit.Of(old), null, ct);
        return true;
    }
}

/// <summary>Set (or clear, ModelId = null) the default model of a role.</summary>
public sealed record SetAiRoleDefaultCommand(string Role, Guid? ModelId) : ICommand<bool>;
public sealed class SetAiRoleDefaultCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<SetAiRoleDefaultCommand, bool>
{
    public async ValueTask<bool> Handle(SetAiRoleDefaultCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var role = AiCatalog.Clean([c.Role], AiCatalog.Roles, "role")[0];
        var beforeId = (await db.ListAiRoleDefaultsAsync(ct)).FirstOrDefault(d => d.Role == role)?.ModelId;
        var before = beforeId is { } bid ? (await db.GetAiModelAsync(bid, ct))?.ModelId : null;
        string? after = null;
        if (c.ModelId is { } mid)
        {
            var m = await db.GetAiModelAsync(mid, ct) ?? throw new NotFoundException("AI_MODEL_NOT_FOUND", "Model not found");
            if (!m.Roles.Contains(role)) throw new PreconditionException("AI_ROLE_MISMATCH", $"Model {m.ModelId} does not serve the {role} role");
            await db.SetAiRoleDefaultAsync(role, mid, uid, clock.UtcNow, ct);
            after = m.ModelId;
        }
        else await db.ClearAiRoleDefaultAsync(role, ct);
        await AiAudit.WriteAsync(db, clock, uid, "role.default", role, before is null ? null : new JsonObject { ["model"] = before },
            after is null ? null : new JsonObject { ["model"] = after }, ct);
        return true;
    }
}

/// <summary>Run recording (internal: written by the api runtime around every AI call).</summary>
public sealed record StartAiRunCommand(Guid Id, Guid? WorkspaceId, string Kind, string Purpose, string? Subject, string Trigger, Guid? ActorId,
    Guid? ModelRef, string? ModelLabel, string SelectionJson, string InputJson) : ICommand<bool>, IInternalCommand;
public sealed class StartAiRunCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<StartAiRunCommand, bool>
{
    public async ValueTask<bool> Handle(StartAiRunCommand c, CancellationToken ct)
    {
        await db.InsertAiRunAsync(c.Id, c.WorkspaceId, c.Kind, c.Purpose, c.Subject, c.Trigger, c.ActorId, c.ModelRef, c.ModelLabel, c.SelectionJson, c.InputJson, clock.UtcNow, ct);
        return true;
    }
}

public sealed record FinishAiRunCommand(Guid Id, string State, string? Fallback, Guid? ModelRef, string? ModelLabel, string OutputJson, string? DecidedBy,
    double? Confidence, int? LatencyMs, int? TokensIn, int? TokensOut, double? CostUsd, string AttemptsJson, string? ErrorCode, string? Error,
    bool UpdateHealth, string? HealthReason) : ICommand<bool>, IInternalCommand;
public sealed class FinishAiRunCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<FinishAiRunCommand, bool>
{
    public async ValueTask<bool> Handle(FinishAiRunCommand c, CancellationToken ct)
    {
        await db.FinishAiRunAsync(c.Id, c.State, c.Fallback, c.ModelRef, c.ModelLabel, c.OutputJson, c.DecidedBy, c.Confidence, c.LatencyMs, c.TokensIn,
            c.TokensOut, c.CostUsd, c.AttemptsJson, c.ErrorCode, c.Error, clock.UtcNow, ct);
        if (c.UpdateHealth && c.ModelRef is { } mid)
            await db.RecordAiModelOutcomeAsync(mid, c.State == "succeeded", c.HealthReason ?? c.Error, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>Startup: runs left "running" by a previous process are marked failed (visible, never stuck).</summary>
public sealed record AbandonStaleAiRunsCommand : ICommand<bool>, IInternalCommand;
public sealed class AbandonStaleAiRunsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<AbandonStaleAiRunsCommand, bool>
{
    public async ValueTask<bool> Handle(AbandonStaleAiRunsCommand c, CancellationToken ct) { await db.AbandonStaleAiRunsAsync(clock.UtcNow, clock.UtcNow, ct); return true; }
}

/// <summary>Health from a real call on a model that was tried but is not the run's final model.</summary>
public sealed record RecordAiModelOutcomeCommand(Guid ModelId, bool Ok, string? Reason) : ICommand<bool>, IInternalCommand;
public sealed class RecordAiModelOutcomeCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordAiModelOutcomeCommand, bool>
{
    public async ValueTask<bool> Handle(RecordAiModelOutcomeCommand c, CancellationToken ct) { await db.RecordAiModelOutcomeAsync(c.ModelId, c.Ok, c.Reason, clock.UtcNow, ct); return true; }
}

public sealed record SetAiLastTestCommand(Guid ModelId, Guid RunId) : ICommand<bool>, IInternalCommand;
public sealed class SetAiLastTestCommandHandler(IWriteSession db) : ICommandHandler<SetAiLastTestCommand, bool>
{
    public async ValueTask<bool> Handle(SetAiLastTestCommand c, CancellationToken ct) { await db.SetAiLastTestAsync(c.ModelId, c.RunId, ct); return true; }
}

/// <summary>Who may see which runs: workspace runs for its members; instance-level runs (model tests) for the Instance Admin.</summary>
internal static class AiRunAccess
{
    public static async Task<(Guid? Ws, bool Instance)> ScopeAsync(IReadSession db, WorkspaceAuthorizer auth, ICurrentUser user, Guid? workspaceId, CancellationToken ct)
    {
        var uid = user.RequireUser();
        if (workspaceId is { } ws) await auth.RequireAsync(ws, "dataset.read", ct);
        var admin = await db.GetUserByIdAsync(uid, ct) is { IsInstanceAdmin: true };
        return (workspaceId, admin);
    }
}

public sealed record ListAiRunsQuery(Guid? WorkspaceId, string State, Guid? ModelRef, int Limit) : IQuery<List<AiRunRow>>;
public sealed class ListAiRunsQueryHandler(IReadSession db, WorkspaceAuthorizer auth, ICurrentUser user) : IQueryHandler<ListAiRunsQuery, List<AiRunRow>>
{
    public async ValueTask<List<AiRunRow>> Handle(ListAiRunsQuery q, CancellationToken ct)
    {
        var (ws, instance) = await AiRunAccess.ScopeAsync(db, auth, user, q.WorkspaceId, ct);
        return await db.ListAiRunsAsync(ws, instance, q.State ?? "", q.ModelRef, Math.Clamp(q.Limit <= 0 ? 100 : q.Limit, 1, 500), ct);
    }
}

public sealed record GetAiRunQuery(Guid Id) : IQuery<AiRunRow>;
public sealed class GetAiRunQueryHandler(IReadSession db, WorkspaceAuthorizer auth, ICurrentUser user) : IQueryHandler<GetAiRunQuery, AiRunRow>
{
    public async ValueTask<AiRunRow> Handle(GetAiRunQuery q, CancellationToken ct)
    {
        var r = await db.GetAiRunAsync(q.Id, ct) ?? throw new NotFoundException("AI_RUN_NOT_FOUND", "AI run not found");
        var (_, instance) = await AiRunAccess.ScopeAsync(db, auth, user, r.WorkspaceId, ct);
        if (r.WorkspaceId is null && !instance) throw new NotFoundException("AI_RUN_NOT_FOUND", "AI run not found");
        return r;
    }
}

public sealed record GetAiRunSummaryQuery(Guid? WorkspaceId) : IQuery<AiRunSummaryRow>;
public sealed class GetAiRunSummaryQueryHandler(IReadSession db, WorkspaceAuthorizer auth, ICurrentUser user, IClock clock) : IQueryHandler<GetAiRunSummaryQuery, AiRunSummaryRow>
{
    public async ValueTask<AiRunSummaryRow> Handle(GetAiRunSummaryQuery q, CancellationToken ct)
    {
        var (ws, instance) = await AiRunAccess.ScopeAsync(db, auth, user, q.WorkspaceId, ct);
        return await db.AiRunSummaryAsync(ws, instance, clock.UtcNow.AddHours(-1), ct) ?? new AiRunSummaryRow(0, 0, 0, null);
    }
}

public sealed record ListAiAuditQuery(int Limit) : IQuery<List<AiAuditRow>>;
public sealed class ListAiAuditQueryHandler(IReadSession db, InstanceAdminGuard guard) : IQueryHandler<ListAiAuditQuery, List<AiAuditRow>>
{
    public async ValueTask<List<AiAuditRow>> Handle(ListAiAuditQuery q, CancellationToken ct)
    {
        await guard.RequireAsync(ct);
        return await db.ListAiAuditAsync(Math.Clamp(q.Limit <= 0 ? 100 : q.Limit, 1, 500), ct);
    }
}
