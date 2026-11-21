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

public sealed record PromptRow(Guid Id, string Name, string Description, DateTimeOffset CreatedAt, int LatestVersion);
public sealed record PromptVersionRow(int Version, string System, string Template, string Variables, Guid? DeploymentId, double Temperature, int MaxTokens,
    string Note, DateTimeOffset CreatedAt);
public sealed record LlmSettingsRow(Guid DeploymentId, string PiiMode, bool BlockInjection, string BlockedTerms, bool CheckOutput, double PricePer1k, DateTimeOffset UpdatedAt);
public sealed record UsageDayRow(DateTimeOffset Day, Guid DeploymentId, string Slug, long Calls, long PromptTokens, long CompletionTokens, double Cost,
    double AvgLatencyMs, long Blocked);
public sealed record EvalSetRow(Guid Id, string Name, string Cases, DateTimeOffset CreatedAt);
public sealed record EvalRunRow(Guid Id, Guid SetId, string SetName, Guid PromptId, string PromptName, int PromptVersion, double Score, string Results, DateTimeOffset CreatedAt);

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

/// <summary>Calls the text generation model on the deployment's replica (implemented by the host with the ModelServer gRPC client).</summary>
public interface ILlmInvoker
{
    Task<(string Text, int PromptTokens, int CompletionTokens)> ChatAsync(string endpoint, IReadOnlyList<(string Role, string Content)> messages,
        int maxTokens, double temperature, CancellationToken ct);
}

/// <summary>
/// Gateway guardrails (M15 §4): PII (email, VN phone number, CCCD/CMND national ID, card) → off / redact / block; prompt injection (English + Vietnamese patterns) → block;
/// Admin-defined banned terms → block. Applied to input, and to output when `check_output` is on.
/// </summary>
public static partial class Guardrails
{
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")] private static partial Regex Email();
    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)(?:[\s.-]?\d){9,10}(?!\d)")] private static partial Regex Phone();
    [GeneratedRegex(@"(?<!\d)\d{12}(?!\d)|(?<!\d)\d{9}(?!\d)")] private static partial Regex NationalId();
    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){13,16}(?!\d)")] private static partial Regex Card();
    [GeneratedRegex(@"ignore (all |any )?(the )?(previous|above|prior) (instructions|prompts?)|disregard (the |all )?(previous|above) |reveal (your |the )?(system prompt|instructions)|you are now (?:in )?(developer|dan) mode|bỏ qua (mọi |tất cả |các )?(hướng dẫn|chỉ dẫn|yêu cầu) (trước|ở trên)|tiết lộ (lời nhắn|prompt) hệ thống",
        RegexOptions.IgnoreCase)]
    private static partial Regex Injection();

    public sealed record Result(string Text, bool Blocked, string? Reason, List<string> Flags);

    public static Result Apply(string text, LlmSettingsRow? s, bool output)
    {
        var flags = new List<string>();
        if (s is null) return new Result(text, false, null, flags);
        var t = text;
        if (!output && s.BlockInjection && Injection().IsMatch(t)) { flags.Add("injection"); return new Result(t, true, "Prompt injection detected", flags); }
        foreach (var term in JsonNode.Parse(s.BlockedTerms)?.AsArray().Select(x => x?.ToString() ?? "").Where(x => x.Length > 0) ?? [])
            if (t.Contains(term, StringComparison.OrdinalIgnoreCase)) { flags.Add("term"); return new Result(t, true, $"Contains a banned term (\"{term}\")", flags); }
        if (s.PiiMode is "redact" or "block")
        {
            foreach (var (name, rx, mask) in new[] { ("email", Email(), "[EMAIL]"), ("card", Card(), "[CARD]"), ("phone", Phone(), "[PHONE]"), ("id", NationalId(), "[ID NUMBER]") })
            {
                if (!rx.IsMatch(t)) continue;
                flags.Add("pii:" + name);
                if (s.PiiMode == "block") return new Result(t, true, $"Contains personal data ({name})", flags);
                t = rx.Replace(t, mask);
            }
        }
        return new Result(t, false, null, flags);
    }
}

public sealed record LlmCallResult(string Text, int PromptTokens, int CompletionTokens, double LatencyMs, double Cost, bool Blocked, string? Reason,
    IReadOnlyList<string> Flags, IReadOnlyList<(string Role, string Content)> SentMessages);

/// <summary>One guarded LLM call + usage recording (tokens, latency, estimated cost). When blocked the model is not called, but `blocked` usage is still recorded.</summary>
public static class LlmCall
{
    public static async Task<LlmCallResult> RunAsync(IWriteSession db, IClock clock, ILlmInvoker invoker, DeploymentRow dep,
        IReadOnlyList<(string Role, string Content)> messages, int maxTokens, double temperature, string source, Guid? promptId, int? promptVersion, CancellationToken ct)
    {
        if (dep.State != "healthy" || dep.Endpoint is null) throw new UnavailableException("SERVE_NOT_READY", $"Deployment {dep.Slug} is {dep.State}");
        var settings = await db.GetLlmSettingsAsync(dep.Id, ct);
        var flags = new List<string>();
        var sent = new List<(string, string)>();
        foreach (var (role, content) in messages)
        {
            var g = Guardrails.Apply(content, settings, output: false);
            flags.AddRange(g.Flags);
            if (g.Blocked) return await Record(db, clock, dep, settings, new LlmCallResult("", 0, 0, 0, 0, true, g.Reason, flags.Distinct().ToList(), sent), source, promptId, promptVersion, ct);
            sent.Add((role, g.Text));
        }
        // Do not hold a pooled connection (or an open transaction) while waiting for the model.
        await db.ReleaseAsync(ct);
        var sw = Stopwatch.StartNew();
        var (text, pt, comp) = await invoker.ChatAsync(dep.Endpoint, sent, maxTokens, temperature, ct);
        var latency = sw.Elapsed.TotalMilliseconds;
        // Model does not report tokens → estimate by whitespace (good enough for observation; the real tokenizer belongs to the recipe).
        if (pt == 0) pt = sent.Sum(m => Words(m.Item2));
        if (comp == 0) comp = Words(text);
        var blocked = false;
        string? reason = null;
        if (settings?.CheckOutput == true)
        {
            var g = Guardrails.Apply(text, settings, output: true);
            flags.AddRange(g.Flags.Select(f => "output:" + f));
            (text, blocked, reason) = g.Blocked ? ("", true, "Output: " + g.Reason) : (g.Text, false, null);
        }
        var cost = (pt + comp) / 1000.0 * (settings?.PricePer1k ?? 0);
        return await Record(db, clock, dep, settings, new LlmCallResult(text, pt, comp, latency, cost, blocked, reason, flags.Distinct().ToList(), sent), source, promptId, promptVersion, ct);
    }

    private static async Task<LlmCallResult> Record(IWriteSession db, IClock clock, DeploymentRow dep, LlmSettingsRow? s, LlmCallResult r, string source,
        Guid? promptId, int? promptVersion, CancellationToken ct)
    {
        await db.InsertUsageAsync(Ids.New(), dep.WorkspaceId, dep.Id, promptId, promptVersion, source, clock.UtcNow, r.PromptTokens, r.CompletionTokens,
            r.LatencyMs, r.Cost, r.Blocked, new JsonArray(r.Flags.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()).ToJsonString(), ct);
        _ = s;
        return r;
    }

    private static int Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>Prompt template: variables `{{name}}` (letters, digits, underscores). Missing variable → a clear error, never an empty string sent to the model.</summary>
public static partial class PromptTemplate
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")] private static partial Regex Var();

    public static List<string> Variables(params string[] texts) => texts.SelectMany(t => Var().Matches(t).Select(m => m.Groups[1].Value)).Distinct().ToList();

    public static string Render(string template, IReadOnlyDictionary<string, string> vars)
    {
        var missing = Var().Matches(template).Select(m => m.Groups[1].Value).Where(v => !vars.ContainsKey(v)).Distinct().ToList();
        if (missing.Count > 0) throw new InvalidArgumentException("LLM_PROMPT_VARIABLE_MISSING", "Missing variables: " + string.Join(", ", missing));
        return Var().Replace(template, m => vars[m.Groups[1].Value]);
    }
}

// ───────────── Prompt registry ─────────────

public sealed record CreatePromptCommand(Guid WorkspaceId, string Name, string Description) : ICommand<PromptRow>;

public sealed class CreatePromptCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreatePromptCommand, PromptRow>
{
    public async ValueTask<PromptRow> Handle(CreatePromptCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        if (!Identity.IdentityRules.Slug().IsMatch(c.Name)) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Name may only contain lowercase letters, numbers and hyphens" });
        var id = Ids.New();
        try { await db.InsertPromptAsync(id, c.WorkspaceId, c.Name, c.Description, uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("LLM_PROMPT_NAME_TAKEN", "Prompt name is already in use"); }
        return (await db.GetPromptAsync(c.WorkspaceId, id.ToString(), ct))!;
    }
}

public sealed record ListPromptsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<PromptRow>>;

public sealed class ListPromptsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPromptsQuery, IReadOnlyList<PromptRow>>
{
    public async ValueTask<IReadOnlyList<PromptRow>> Handle(ListPromptsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListPromptsAsync(q.WorkspaceId, ct);
    }
}

public sealed record GetPromptQuery(Guid WorkspaceId, string Key) : IQuery<(PromptRow Prompt, IReadOnlyList<PromptVersionRow> Versions)>;

public sealed class GetPromptQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetPromptQuery, (PromptRow Prompt, IReadOnlyList<PromptVersionRow> Versions)>
{
    public async ValueTask<(PromptRow Prompt, IReadOnlyList<PromptVersionRow> Versions)> Handle(GetPromptQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var p = await db.GetPromptAsync(q.WorkspaceId, q.Key, ct) ?? throw new NotFoundException("LLM_PROMPT_NOT_FOUND", "Prompt not found");
        var vs = await db.ListPromptVersionsAsync(p.Id, ct);
        return (p with { LatestVersion = vs.Count > 0 ? vs[0].Version : 0 }, vs);
    }
}

public sealed record CreatePromptVersionCommand(Guid WorkspaceId, string PromptKey, string System, string Template, Guid? DeploymentId, double Temperature,
    int MaxTokens, string Note) : ICommand<PromptVersionRow>;

public sealed class CreatePromptVersionCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreatePromptVersionCommand, PromptVersionRow>
{
    public async ValueTask<PromptVersionRow> Handle(CreatePromptVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var p = await db.GetPromptAsync(c.WorkspaceId, c.PromptKey, ct) ?? throw new NotFoundException("LLM_PROMPT_NOT_FOUND", "Prompt not found");
        if (string.IsNullOrWhiteSpace(c.Template)) throw new ValidationException(new Dictionary<string, string> { ["template"] = "Template is empty" });
        if (c.DeploymentId is { } d) _ = await db.GetDeploymentAsync(d, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var vars = PromptTemplate.Variables(c.System ?? "", c.Template);
        var v = await db.InsertPromptVersionAsync(p.Id, c.System ?? "", c.Template, new JsonArray(vars.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()).ToJsonString(),
            c.DeploymentId, c.Temperature is >= 0 and <= 2 ? c.Temperature : 0.7, c.MaxTokens is > 0 and <= 32768 ? c.MaxTokens : 512, c.Note ?? "", uid, clock.UtcNow, ct);
        return (await db.ListPromptVersionsAsync(p.Id, ct)).First(x => x.Version == v);
    }
}

/// <summary>Runs a prompt version on an LLM deployment (the version's default, or an override) — used for quick tests and version comparison.</summary>
public sealed record RunPromptCommand(Guid WorkspaceId, string PromptKey, int Version, IReadOnlyDictionary<string, string> Variables, Guid? DeploymentId)
    : ICommand<(PromptVersionRow Version, LlmCallResult Result)>;

public sealed class RunPromptCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, ILlmInvoker invoker)
    : ICommandHandler<RunPromptCommand, (PromptVersionRow Version, LlmCallResult Result)>
{
    public async ValueTask<(PromptVersionRow Version, LlmCallResult Result)> Handle(RunPromptCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var (p, v) = await PromptLookup.ResolveAsync(db, c.WorkspaceId, c.PromptKey, c.Version, ct);
        var dep = await PromptLookup.DeploymentAsync(db, c.WorkspaceId, c.DeploymentId ?? v.DeploymentId, ct);
        var r = await LlmCall.RunAsync(db, clock, invoker, dep, PromptLookup.Messages(v, c.Variables), v.MaxTokens, v.Temperature, "prompt", p.Id, v.Version, ct);
        return (v, r);
    }
}

internal static class PromptLookup
{
    public static async Task<(PromptRow, PromptVersionRow)> ResolveAsync(IReadSession db, Guid ws, string key, int version, CancellationToken ct)
    {
        var p = await db.GetPromptAsync(ws, key, ct) ?? throw new NotFoundException("LLM_PROMPT_NOT_FOUND", "Prompt not found");
        var vs = await db.ListPromptVersionsAsync(p.Id, ct);
        var v = (version > 0 ? vs.FirstOrDefault(x => x.Version == version) : vs.FirstOrDefault())
                ?? throw new NotFoundException("LLM_PROMPT_VERSION_NOT_FOUND", "Prompt has no such version");
        return (p, v);
    }

    public static async Task<DeploymentRow> DeploymentAsync(IReadSession db, Guid ws, Guid? id, CancellationToken ct) =>
        id is { } d ? await db.GetDeploymentAsync(d, ws, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found")
                    : throw new InvalidArgumentException("LLM_NO_DEPLOYMENT", "Prompt version has no deployment — choose an LLM deployment");

    public static List<(string, string)> Messages(PromptVersionRow v, IReadOnlyDictionary<string, string> vars)
    {
        var m = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(v.System)) m.Add(("system", PromptTemplate.Render(v.System, vars)));
        m.Add(("user", PromptTemplate.Render(v.Template, vars)));
        return m;
    }
}

// ───────────── Guardrails + pricing per deployment ─────────────

public sealed record GetLlmSettingsQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<LlmSettingsRow>;

public sealed class GetLlmSettingsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetLlmSettingsQuery, LlmSettingsRow>
{
    public async ValueTask<LlmSettingsRow> Handle(GetLlmSettingsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.GetLlmSettingsAsync(q.DeploymentId, ct) ?? new LlmSettingsRow(q.DeploymentId, "off", false, "[]", false, 0, DateTimeOffset.MinValue);
    }
}

public sealed record UpdateLlmSettingsCommand(Guid WorkspaceId, Guid DeploymentId, string PiiMode, bool BlockInjection, IReadOnlyList<string> BlockedTerms,
    bool CheckOutput, double PricePer1k) : ICommand<LlmSettingsRow>;

public sealed class UpdateLlmSettingsCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<UpdateLlmSettingsCommand, LlmSettingsRow>
{
    public async ValueTask<LlmSettingsRow> Handle(UpdateLlmSettingsCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var mode = string.IsNullOrEmpty(c.PiiMode) ? "off" : c.PiiMode;
        if (mode is not ("off" or "redact" or "block")) throw new ValidationException(new Dictionary<string, string> { ["pii_mode"] = "off | redact | block" });
        var terms = new JsonArray(c.BlockedTerms.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().Take(500).Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
        await db.UpsertLlmSettingsAsync(c.DeploymentId, c.WorkspaceId, mode, c.BlockInjection, terms.ToJsonString(), c.CheckOutput, Math.Max(0, c.PricePer1k), uid, clock.UtcNow, ct);
        return (await db.GetLlmSettingsAsync(c.DeploymentId, ct))!;
    }
}

/// <summary>REST gateway `{"messages":[…]}`: same guardrails + usage recording as prompt calls (no user, no audit).</summary>
public sealed record GatewayChatCommand(DeploymentRow Deployment, IReadOnlyList<(string Role, string Content)> Messages, int MaxTokens, double Temperature)
    : ICommand<LlmCallResult>, IInternalCommand;

public sealed class GatewayChatCommandHandler(IWriteSession db, IClock clock, ILlmInvoker invoker) : ICommandHandler<GatewayChatCommand, LlmCallResult>
{
    public async ValueTask<LlmCallResult> Handle(GatewayChatCommand c, CancellationToken ct) =>
        await LlmCall.RunAsync(db, clock, invoker, c.Deployment, c.Messages, c.MaxTokens > 0 ? c.MaxTokens : 512, c.Temperature, "gateway", null, null, ct);
}

public sealed record GetLlmUsageQuery(Guid WorkspaceId, int Days) : IQuery<IReadOnlyList<UsageDayRow>>;

public sealed class GetLlmUsageQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<GetLlmUsageQuery, IReadOnlyList<UsageDayRow>>
{
    public async ValueTask<IReadOnlyList<UsageDayRow>> Handle(GetLlmUsageQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.UsageByDayAsync(q.WorkspaceId, clock.UtcNow.AddDays(-(q.Days is > 0 and <= 365 ? q.Days : 30)), ct);
    }
}

// ───────────── Eval ─────────────

/// <summary>Eval set: `[{"vars":{…}, "expect":{"contains","not_contains","equals","regex","max_chars"}}]` (≤ 100 cases).</summary>
public sealed record CreateEvalSetCommand(Guid WorkspaceId, string Name, string CasesJson) : ICommand<EvalSetRow>;

public sealed class CreateEvalSetCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateEvalSetCommand, EvalSetRow>
{
    public async ValueTask<EvalSetRow> Handle(CreateEvalSetCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        JsonArray cases;
        try { cases = JsonNode.Parse(c.CasesJson) as JsonArray ?? throw new JsonException(); }
        catch (JsonException) { throw new ValidationException(new Dictionary<string, string> { ["cases"] = "Expected a JSON array of cases" }); }
        if (cases.Count is 0 or > 100) throw new ValidationException(new Dictionary<string, string> { ["cases"] = "1–100 case" });
        foreach (var (node, i) in cases.Select((n, i) => (n, i)))
        {
            if (node is not JsonObject o || o["vars"] is not JsonObject || o["expect"] is not JsonObject e || e.Count == 0)
                throw new ValidationException(new Dictionary<string, string> { ["cases"] = $"Case {i}: requires vars (object) and expect (object, ≥ 1 condition)" });
            if (e["regex"]?.ToString() is { } rx)
                try { _ = new Regex(rx, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { throw new ValidationException(new Dictionary<string, string> { ["cases"] = $"Case {i}: invalid regex" }); }
        }
        var id = Ids.New();
        try { await db.InsertEvalSetAsync(id, c.WorkspaceId, c.Name.Trim(), c.CasesJson, uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("LLM_EVAL_SET_NAME_TAKEN", "Eval set name is already in use"); }
        return (await db.GetEvalSetAsync(id, c.WorkspaceId, ct))!;
    }
}

public sealed record ListEvalSetsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<EvalSetRow>>;

public sealed class ListEvalSetsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEvalSetsQuery, IReadOnlyList<EvalSetRow>>
{
    public async ValueTask<IReadOnlyList<EvalSetRow>> Handle(ListEvalSetsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListEvalSetsAsync(q.WorkspaceId, ct);
    }
}

/// <summary>Runs an eval set against a prompt version (synchronous, ≤ 100 cases) → score = share of cases passing every condition; each call records `eval` usage.</summary>
public sealed record RunEvalCommand(Guid WorkspaceId, Guid SetId, string PromptKey, int Version, Guid? DeploymentId) : ICommand<EvalRunRow>;

public sealed class RunEvalCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, ILlmInvoker invoker) : ICommandHandler<RunEvalCommand, EvalRunRow>
{
    public async ValueTask<EvalRunRow> Handle(RunEvalCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var set = await db.GetEvalSetAsync(c.SetId, c.WorkspaceId, ct) ?? throw new NotFoundException("LLM_EVAL_SET_NOT_FOUND", "Eval set not found");
        var (p, v) = await PromptLookup.ResolveAsync(db, c.WorkspaceId, c.PromptKey, c.Version, ct);
        var dep = await PromptLookup.DeploymentAsync(db, c.WorkspaceId, c.DeploymentId ?? v.DeploymentId, ct);
        var results = new JsonArray();
        var passed = 0;
        var cases = JsonNode.Parse(set.Cases)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
        {
            var cs = cases[i]!.AsObject();
            var vars = cs["vars"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
            var checks = new JsonObject();
            string output;
            LlmCallResult? r = null;
            try
            {
                r = await LlmCall.RunAsync(db, clock, invoker, dep, PromptLookup.Messages(v, vars), v.MaxTokens, v.Temperature, "eval", p.Id, v.Version, ct);
                output = r.Text;
                if (r.Blocked) checks["guardrail"] = false;
            }
            catch (InvalidArgumentException e) { output = ""; checks["render"] = false; checks["error"] = e.Message; }
            foreach (var (k, ev) in cs["expect"]!.AsObject())
            {
                var want = ev?.ToString() ?? "";
                checks[k] = k switch
                {
                    "contains" => output.Contains(want, StringComparison.OrdinalIgnoreCase),
                    "not_contains" => !output.Contains(want, StringComparison.OrdinalIgnoreCase),
                    "equals" => string.Equals(output.Trim(), want.Trim(), StringComparison.Ordinal),
                    "regex" => Regex.IsMatch(output, want, RegexOptions.None, TimeSpan.FromMilliseconds(200)),
                    "max_chars" => int.TryParse(want, out var n) && output.Length <= n,
                    _ => false,
                };
            }
            var ok = checks.All(kv => kv.Value is not JsonValue b || !b.TryGetValue<bool>(out var x) || x);
            if (ok) passed++;
            results.Add((JsonNode)new JsonObject
            {
                ["index"] = i, ["output"] = output, ["passed"] = ok, ["checks"] = checks,
                ["tokens"] = (r?.PromptTokens ?? 0) + (r?.CompletionTokens ?? 0), ["latency_ms"] = Math.Round(r?.LatencyMs ?? 0, 1),
            });
        }
        var id = Ids.New();
        var score = Math.Round(passed / (double)Math.Max(1, cases.Count), 4);
        var now = clock.UtcNow;
        await db.InsertEvalRunAsync(id, c.WorkspaceId, set.Id, p.Id, v.Version, score, results.ToJsonString(), uid, now, ct);
        return new EvalRunRow(id, set.Id, set.Name, p.Id, p.Name, v.Version, score, results.ToJsonString(), now);
    }
}

public sealed record ListEvalRunsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<EvalRunRow>>;

public sealed class ListEvalRunsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEvalRunsQuery, IReadOnlyList<EvalRunRow>>
{
    public async ValueTask<IReadOnlyList<EvalRunRow>> Handle(ListEvalRunsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListEvalRunsAsync(q.WorkspaceId, ct);
    }
}
