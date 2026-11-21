using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.Cqrs;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>The one entry point for AI calls. Singleton; DB writes go through the mediator in a fresh scope.</summary>
public sealed class AiRuntime(IServiceScopeFactory scopes, AiProviders providers, AiCircuits circuits, AiLimiter limiter, SecretBox box, IClock clock,
    ILogger<AiRuntime> log)
{
    private (DateTimeOffset At, AiOverview Value)? _catalog;
    private readonly Lock _gate = new();
    private const int MaxStored = 200_000;

    public AiCircuits Circuits => circuits;
    public AiLimiter Limiter => limiter;

    public void Invalidate() { lock (_gate) _catalog = null; }

    public async Task<AiOverview> CatalogAsync(CancellationToken ct)
    {
        lock (_gate) if (_catalog is { } c && clock.UtcNow - c.At < TimeSpan.FromSeconds(5)) return c.Value;
        await using var scope = scopes.CreateAsyncScope();
        var v = await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new GetAiCatalogInternalQuery(), ct);
        lock (_gate) _catalog = (clock.UtcNow, v);
        return v;
    }

    public string? KeyOf(AiConnectionRow c) => box.Open(c.SecretEnc);

    public static string Label(AiModelRow m, AiConnectionRow? c) =>
        (m.DisplayName.Length > 0 ? m.DisplayName : m.ModelId) + (c is null ? "" : $" · {c.Name}");

    // ---------- Selection: every model is listed with the reason it was ranked or excluded ----------

    public sealed record Candidate(AiModelRow Model, AiConnectionRow Connection, bool Eligible, string Reason, int Rank);

    public List<Candidate> Select(AiOverview cat, AiCallSpec spec)
    {
        var conns = cat.Connections.ToDictionary(c => c.Id);
        var def = cat.Defaults.FirstOrDefault(d => d.Role == spec.Role)?.ModelId;
        var stats = cat.Stats.ToDictionary(s => s.ModelId);
        var need = spec.NeedInputs ?? [];
        var decision = spec.Kind == "decision";
        var list = new List<(AiModelRow M, AiConnectionRow C, bool Ok, string Why, double Score)>();
        foreach (var m in cat.Models)
        {
            if (!conns.TryGetValue(m.ConnectionId, out var c)) continue;
            string? why = null;
            if (spec.PinnedModel is { } pin && pin != m.Id) why = "Not used: another model was chosen for this call";
            else if (!m.Roles.Contains(spec.Role) && spec.PinnedModel is null) why = $"Excluded: does not serve the {spec.Role} role";
            else if (!m.IsActive) why = "Excluded: model is turned off";
            else if (!c.IsActive) why = $"Excluded: connection {c.Name} is turned off";
            else if (AiProviders.KeyRequired(c.ProviderType) && c.SecretEnc is null) why = $"Excluded: connection {c.Name} has no API key";
            else if (decision != AiCatalog.IsDecisionServer(c.ProviderType))
                why = decision ? "Excluded: not a decision server (typed answers need System One)" : "Excluded: a decision server cannot answer chat prompts";
            else if (need.FirstOrDefault(i => !m.Inputs.Contains(i)) is { } miss) why = $"Excluded: does not accept {miss} input";
            else if (circuits.Describe(m.Id) == "open") why = $"Excluded: paused after {m.ConsecutiveFailures} failures in a row (circuit open, retries in 30 s)";
            if (why is not null) { list.Add((m, c, false, why, 0)); continue; }

            // Rank: pinned > role default > health > measured speed > price. Lower is better.
            var score = 0.0;
            var reasons = new List<string>();
            if (spec.PinnedModel == m.Id) { score -= 1000; reasons.Add("Chosen for this call"); }
            if (def == m.Id) { score -= 500; reasons.Add($"Default for role {spec.Role}"); }
            score += m.Health switch { "ok" => 0, "unknown" => 30, "degraded" => 60, _ => 120 };
            reasons.Add(m.Health switch { "ok" => "healthy", "unknown" => "not tested yet", "degraded" => "degraded: " + m.HealthReason, _ => "failed: " + m.HealthReason });
            if (stats.TryGetValue(m.Id, out var st) && st.Runs > 0) { score += st.P50Ms / 100.0; reasons.Add($"p50 {st.P50Ms} ms"); }
            if (m.InputUsdPer1m is { } pi) score += pi / 10;
            list.Add((m, c, true, string.Join(" · ", reasons), score));
        }
        var rank = 0;
        return [.. list.OrderBy(x => x.Ok ? 0 : 1).ThenBy(x => x.Score).Select(x => new Candidate(x.M, x.C, x.Ok, x.Why, x.Ok ? ++rank : 0))];
    }

    // ---------- Calls ----------

    public Task<AiResult> ChatAsync(AiCallSpec spec, IReadOnlyList<AiMessage> messages, CancellationToken ct)
    {
        // Images are not copied into the record (size); their type and size are, so the record still says exactly what was sent.
        var input = new JsonObject { ["messages"] = new JsonArray([.. messages.Select(m =>
        {
            var o = new JsonObject { ["role"] = m.Role, ["content"] = Store(m.Content) };
            if (m.Images is { Count: > 0 })
                o["images"] = new JsonArray([.. m.Images.Select(i => (JsonNode)new JsonObject { ["type"] = i.Mime, ["bytes"] = i.Data.Length, ["stored"] = false })]);
            return (JsonNode)o;
        })]) };
        return RunAsync(spec, input, async (cand, key, token) =>
        {
            var limit = Math.Min(spec.MaxTokens ?? cand.Model.MaxTokens, cand.Model.MaxTokens);
            var r = await providers.ChatAsync(cand.Connection, key, cand.Model.ModelId, messages, limit, token);
            // An empty answer is a failure, never a silent success. Reasoning models can spend the whole budget thinking.
            if (string.IsNullOrWhiteSpace(r.Text))
                throw new AiCallException("empty-answer", r.Reasoning is not null
                    ? $"The model returned no answer text: it used {r.TokensOut} of {limit} output tokens on reasoning (stop: {r.StopReason}). Raise Max output tokens or use a non-reasoning model. Reasoning: {AiProviders.Clip(r.Reasoning.ReplaceLineEndings(" "), 400)}"
                    : $"The model returned no answer text (stop: {r.StopReason}, {r.TokensOut} output tokens)", r.Status, r.TokensIn, r.TokensOut);
            var outJson = new JsonObject { ["text"] = Store(r.Text), ["summary"] = AiProviders.Clip(r.Text.ReplaceLineEndings(" ").Trim(), 160), ["stop_reason"] = r.StopReason };
            if (r.Reasoning is not null) outJson["reasoning"] = Store(r.Reasoning);
            return (outJson, r.TokensIn, r.TokensOut, null);
        }, null, ct);
    }

    /// <summary>Typed decision with a hard deadline. heuristic = what to answer when no model can (recorded as decided_by heuristic).</summary>
    public Task<AiResult> DecideAsync(AiCallSpec spec, string state, IReadOnlyList<AiQuestion> questions, Func<JsonObject>? heuristic, CancellationToken ct)
    {
        var input = DecisionInput(state, questions);
        return RunAsync(spec with { DeadlineMs = spec.DeadlineMs ?? 1500 }, input, async (cand, key, token) =>
        {
            var r = await providers.DecideAsync(cand.Connection, key, cand.Model.ModelId, state, questions, token);
            var conf = r.Answers.Select(a => a.Value?["confidence"]).OfType<JsonValue>().Select(v => v.GetValue<double>()).DefaultIfEmpty(-1).Min();
            return (new JsonObject { ["answers"] = r.Answers, ["summary"] = Summarize(r.Answers) }, r.TokensIn, r.TokensOut, conf >= 0 ? conf : null);
        }, heuristic, ct);
    }

    public static JsonObject DecisionInput(string state, IReadOnlyList<AiQuestion> questions) => new()
    {
        ["state"] = Store(state),
        ["questions"] = new JsonArray([.. questions.Select(q => (JsonNode)new JsonObject
        {
            ["id"] = q.Id, ["type"] = q.Type, ["instructions"] = q.Instructions,
            ["options"] = new JsonArray([.. q.Options.Select(o => (JsonNode)new JsonObject { ["key"] = o.Key, ["description"] = o.Description })]),
        })]),
    };

    public static string Summarize(JsonObject answers) => string.Join(", ", answers.Select(a =>
    {
        var v = a.Value!;
        var val = v["choice"]?.ToString() ?? v["score"]?.ToString() ?? v["noul"]?.ToString() ?? "?";
        return v["confidence"] is JsonValue c ? $"{a.Key}={val} ({c.GetValue<double>():0.00})" : $"{a.Key}={val}";
    }));

    private static string Store(string s) => s.Length <= MaxStored ? s : s[..MaxStored] + $"… [truncated, {s.Length} chars in total]";

    private delegate Task<(JsonObject Output, int TokensIn, int TokensOut, double? Confidence)> Call(Candidate c, string? key, CancellationToken ct);

    private async Task<AiResult> RunAsync(AiCallSpec spec, JsonObject input, Call call, Func<JsonObject>? heuristic, CancellationToken ct)
    {
        var cat = await CatalogAsync(ct);
        var cands = Select(cat, spec);
        var eligible = cands.Where(c => c.Eligible).Take(spec.PinnedModel is null ? 2 : 1).ToList();   // at most one fallback model
        var first = eligible.FirstOrDefault();
        var selection = new JsonObject
        {
            ["role"] = spec.Role,
            ["note"] = first is null ? $"No usable model for role {spec.Role}"
                : spec.PinnedModel is not null ? "Model chosen explicitly for this call"
                : cat.Defaults.Any(d => d.Role == spec.Role && d.ModelId == first.Model.Id) ? $"Role default for {spec.Role}"
                : $"No usable default for role {spec.Role}; best eligible model used",
            ["deadline_ms"] = spec.DeadlineMs,
            ["candidates"] = new JsonArray([.. cands.Select(c => (JsonNode)new JsonObject
            {
                ["model_id"] = c.Model.Id.ToString(), ["label"] = Label(c.Model, c.Connection), ["eligible"] = c.Eligible, ["reason"] = c.Reason, ["rank"] = c.Rank,
            })]),
        };
        var runId = Ids.New();
        await SendAsync(new StartAiRunCommand(runId, spec.WorkspaceId, spec.Kind, spec.Purpose, spec.Subject, spec.Trigger, spec.ActorId, first?.Model.Id,
            first is null ? null : Label(first.Model, first.Connection), selection.ToJsonString(), input.ToJsonString()));

        var total = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (spec.DeadlineMs is { } dl) deadline.CancelAfter(dl);
        var attempts = new JsonArray();
        string? lastCode = null, lastError = null, fallback = null;
        var n = 0;
        int spentIn = 0, spentOut = 0;   // tokens billed by failed attempts
        foreach (var cand in eligible)
        {
            n++;
            if (n > 1) fallback = $"{Label(first!.Model, first.Connection)} failed ({lastCode}: {lastError}) → used {Label(cand.Model, cand.Connection)}";
            var w = Stopwatch.StartNew();
            var status = 0;
            try
            {
                if (deadline.IsCancellationRequested) throw new AiCallException("timeout", $"The {spec.DeadlineMs} ms deadline passed before this model was tried");
                if (!circuits.TryEnter(cand.Model.Id)) throw new AiCallException("circuit-open", "Paused after repeated failures; retries in 30 s");
                using var slot = await limiter.AcquireAsync(cand.Connection, deadline.Token);
                using var callCts = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                callCts.CancelAfter(cand.Connection.TimeoutMs);
                var (output, tin, tout, conf) = await call(cand, KeyOf(cand.Connection), callCts.Token);
                status = 200;
                circuits.Report(cand.Model.Id, true);
                attempts.Add((JsonNode)Attempt(n, cand, status, (int)w.ElapsedMilliseconds, "ok", null));
                tin += spentIn; tout += spentOut;
                var cost = cand.Model.InputUsdPer1m is { } pi && cand.Model.OutputUsdPer1m is { } po ? (tin * pi + tout * po) / 1_000_000.0 : (double?)null;
                var latency = (int)total.ElapsedMilliseconds;
                await FinishAsync(new FinishAiRunCommand(runId, "succeeded", fallback, cand.Model.Id, Label(cand.Model, cand.Connection), output.ToJsonString(), "model",
                    conf, latency, tin, tout, cost, attempts.ToJsonString(), null, null, spec.UpdateHealth, null), eligible.Take(n - 1), spec.UpdateHealth, lastError);
                return new AiResult(runId, true, output["text"]?.ToString(), output["answers"] as JsonObject, "model", conf, fallback, null, null, latency,
                    cand.Model.Id, Label(cand.Model, cand.Connection));
            }
            catch (Exception e) when (e is AiCallException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                var ex = e as AiCallException ?? new AiCallException("timeout", deadline.IsCancellationRequested
                    ? $"No answer within the {spec.DeadlineMs} ms deadline" : $"No answer within the connection timeout ({cand.Connection.TimeoutMs} ms)");
                if (ex.Outcome is not ("circuit-open" or "busy" or "rate-limited")) circuits.Report(cand.Model.Id, false);
                status = ex.Status;
                lastCode = ex.Outcome;
                lastError = ex.Message;
                spentIn += ex.TokensIn; spentOut += ex.TokensOut;
                attempts.Add((JsonNode)Attempt(n, cand, status, (int)w.ElapsedMilliseconds, ex.Outcome, ex.Message));
                log.LogInformation("AI run {Run}: {Model} failed with {Outcome}: {Error}", runId, cand.Model.ModelId, ex.Outcome, ex.Message);
            }
        }

        var latencyAll = (int)total.ElapsedMilliseconds;
        var failedModel = eligible.LastOrDefault();
        if (heuristic is not null)
        {
            var h = heuristic();
            var why = first is null ? $"No usable model for role {spec.Role}" : $"Model call failed ({lastCode}: {lastError})";
            var output = new JsonObject { ["answers"] = h, ["summary"] = Summarize(h) };
            await FinishAsync(new FinishAiRunCommand(runId, "succeeded", why + " → answered by built-in rules", failedModel?.Model.Id,
                failedModel is null ? null : Label(failedModel.Model, failedModel.Connection), output.ToJsonString(), "heuristic", null, latencyAll, spentIn, spentOut, null,
                attempts.ToJsonString(), lastCode, lastError, spec.UpdateHealth && failedModel is not null, lastError), eligible.SkipLast(1), spec.UpdateHealth, lastError);
            return new AiResult(runId, true, null, h, "heuristic", null, why, lastCode, lastError, latencyAll, null, null);
        }
        var code = first is null ? "NO_MODEL" : lastCode;
        var error = first is null
            ? (cands.Count == 0 ? "No AI model is declared yet" : $"No usable model for role {spec.Role}: " + string.Join("; ", cands.Select(c => $"{c.Model.ModelId} — {c.Reason}")))
            : lastError;
        await FinishAsync(new FinishAiRunCommand(runId, "failed", null, failedModel?.Model.Id, failedModel is null ? null : Label(failedModel.Model, failedModel.Connection),
            new JsonObject().ToJsonString(), "none", null, latencyAll, spentIn, spentOut, null, attempts.ToJsonString(), code, error, spec.UpdateHealth && failedModel is not null, error),
            eligible.SkipLast(1), spec.UpdateHealth, lastError);
        return new AiResult(runId, false, null, null, "none", null, null, code, error, latencyAll, failedModel?.Model.Id,
            failedModel is null ? null : Label(failedModel.Model, failedModel.Connection));
    }

    private static JsonObject Attempt(int n, Candidate c, int status, int ms, string outcome, string? detail) => new()
    {
        ["number"] = n, ["model_id"] = c.Model.Id.ToString(), ["label"] = Label(c.Model, c.Connection), ["status"] = status, ["latency_ms"] = ms,
        ["outcome"] = outcome, ["detail"] = detail,
    };

    private async Task FinishAsync(FinishAiRunCommand finish, IEnumerable<Candidate> failedEarlier, bool updateHealth, string? reason)
    {
        await SendAsync(finish);
        if (updateHealth) foreach (var c in failedEarlier) await SendAsync(new RecordAiModelOutcomeCommand(c.Model.Id, false, reason));
        Invalidate();
    }

    /// <summary>Recording must not be lost when the caller's request is cancelled.</summary>
    private async Task SendAsync<T>(ICommand<T> cmd)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(cmd, CancellationToken.None);
    }

    // ---------- Model test (manual from the UI, or auto-recovery) ----------

    public const string TestState =
        "Annotation task: sentiment of a product review (positive / negative / neutral).\n" +
        "Review text: \"The battery died after two days and support never answered my emails.\"\n" +
        "Label given by the annotator: positive.";

    public static readonly AiQuestion[] TestQuestions =
    [
        new("label_check", "choice", "Does the annotator's label match the review text?",
        [
            new("correct", "The label matches the text"),
            new("wrong", "The label contradicts the text"),
            new("unsure", "The text does not allow a decision"),
        ]),
    ];

    public async Task<AiResult> TestAsync(Guid modelId, string? prompt, Guid? actor, string trigger, CancellationToken ct)
    {
        Invalidate();
        var cat = await CatalogAsync(ct);
        var m = cat.Models.FirstOrDefault(x => x.Id == modelId) ?? throw new NotFoundException("AI_MODEL_NOT_FOUND", "Model not found");
        var c = cat.Connections.First(x => x.Id == m.ConnectionId);
        circuits.Reset(m.Id);   // a test is an explicit probe: it always goes out
        var decision = AiCatalog.IsDecisionServer(c.ProviderType);
        var spec = new AiCallSpec(decision ? "decision" : "chat", m.Roles.FirstOrDefault() ?? "analyst", "Model test", Label(m, c), trigger, null, actor,
            PinnedModel: m.Id, DeadlineMs: Math.Min(c.TimeoutMs, 60_000), MaxTokens: Math.Min(m.MaxTokens, 2048));
        var result = decision
            ? await DecideAsync(spec with { DeadlineMs = Math.Min(c.TimeoutMs, 10_000) }, TestState, TestQuestions, null, ct)
            : await ChatAsync(spec, [
                new AiMessage("system", "You are being tested by the MLPlatform AI page. Answer in one short sentence."),
                new AiMessage("user", string.IsNullOrWhiteSpace(prompt) ? "Say that you are working and name the model you are." : prompt.Trim()),
            ], ct);
        await SendAsync(new SetAiLastTestCommand(m.Id, result.RunId));
        Invalidate();
        return result;
    }

    // ---------- Readiness per role (top of the AI page) ----------

    public static readonly (string Role, string UsedBy)[] ReadinessRoles =
    [
        ("decision", "Fast label checks and auto-accept (Hermes)"),
        ("labeler", "Label proposals for text, audio and video"),
        ("vision", "Label proposals for images"),
        ("analyst", "Explanations, quality reports and summaries"),
        ("judge", "Second opinion on disputed labels"),
    ];

    public sealed record Readiness(string Role, string State, Guid? ModelId, string? ModelLabel, List<string> Reasons, string UsedBy);

    public List<Readiness> ReadinessOf(AiOverview cat)
    {
        var conns = cat.Connections.ToDictionary(c => c.Id);
        var res = new List<Readiness>();
        foreach (var (role, usedBy) in ReadinessRoles)
        {
            var reasons = new List<string>();
            var defId = cat.Defaults.FirstOrDefault(d => d.Role == role)?.ModelId;
            var m = defId is null ? null : cat.Models.FirstOrDefault(x => x.Id == defId);
            if (m is null)
            {
                var serving = cat.Models.Count(x => x.Roles.Contains(role));
                reasons.Add(serving == 0 ? $"No model declares the {role} role" : $"No default model chosen ({serving} model(s) can serve this role)");
                res.Add(new Readiness(role, "missing", null, null, reasons, usedBy));
                continue;
            }
            var c = conns[m.ConnectionId];
            var state = "ready";
            if (!m.IsActive) { reasons.Add("The default model is turned off"); state = "missing"; }
            if (!c.IsActive) { reasons.Add($"Connection {c.Name} is turned off"); state = "missing"; }
            if (AiProviders.KeyRequired(c.ProviderType) && c.SecretEnc is null) { reasons.Add($"Connection {c.Name} has no API key"); state = "missing"; }
            if (m.Health == "failed") { reasons.Add($"Failed {m.ConsecutiveFailures} times in a row: {m.HealthReason}"); state = "missing"; }
            else if (m.Health == "degraded") { reasons.Add($"Recent failure: {m.HealthReason}"); if (state == "ready") state = "degraded"; }
            else if (m.Health == "unknown") { reasons.Add("Not tested yet — run a test to confirm it answers"); if (state == "ready") state = "degraded"; }
            if (circuits.Describe(m.Id) == "open") { reasons.Add("Paused after repeated failures (circuit open)"); if (state == "ready") state = "degraded"; }
            if (state == "ready") reasons.Add(m.HealthAt is { } at ? $"Healthy, last checked {at:yyyy-MM-dd HH:mm} UTC" : "Healthy");
            res.Add(new Readiness(role, state, m.Id, Label(m, c), reasons, usedBy));
        }
        return res;
    }
}
