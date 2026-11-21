using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Pipelines;

public sealed record PipelineRow(Guid Id, Guid WorkspaceId, string Name, string Definition, string Trigger, bool Enabled, DateTimeOffset? LastTriggeredAt,
    Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record PipelineRunRow(Guid Id, Guid PipelineId, string PipelineName, Guid WorkspaceId, int Number, string State, string Params, string Trigger,
    string? Error, Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);
public sealed record StepRow(string StepId, string Kind, string State, int Attempt, Guid? RefId, string Output, string? Error,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

public static partial class PipelineSql
{
    [SqlQuery("Pipeline/InsertPipeline.sql")]
    public static partial Task InsertPipelineAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, string definition, string trigger,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/GetPipeline.sql")]
    public static partial Task<PipelineRow?> GetPipelineAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Pipeline/ListPipelines.sql")]
    public static partial Task<List<PipelineRow>> ListPipelinesAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Pipeline/ListTriggered.sql")]
    public static partial Task<List<PipelineRow>> ListTriggeredAsync(this IReadSession db, DateTimeOffset now, string eventKind, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Pipeline/MarkTriggered.sql")]
    public static partial Task MarkTriggeredAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/SetPipelineEnabled.sql")]
    public static partial Task SetPipelineEnabledAsync(this IWriteSession db, Guid id, bool enabled, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/InsertRun.sql")]
    public static partial Task<int> InsertRunAsync(this IWriteSession db, Guid id, Guid pipelineId, Guid workspaceId, string runParams, string trigger,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/GetRun.sql")]
    public static partial Task<PipelineRunRow?> GetPipelineRunAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Pipeline/ListRuns.sql")]
    public static partial Task<List<PipelineRunRow>> ListPipelineRunsAsync(this IReadSession db, Guid workspaceId, Guid? pipelineId, CancellationToken ct);
    [SqlQuery("Pipeline/ListActiveRuns.sql")]
    public static partial Task<List<PipelineRunRow>> ListActivePipelineRunsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Pipeline/FinishRun.sql")]
    public static partial Task FinishPipelineRunAsync(this IWriteSession db, Guid id, string state, string? error, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/ReopenRun.sql")]
    public static partial Task ReopenPipelineRunAsync(this IWriteSession db, Guid id, string runParams, CancellationToken ct);
    [SqlQuery("Pipeline/InsertStep.sql")]
    public static partial Task InsertStepAsync(this IWriteSession db, Guid runId, string stepId, string kind, CancellationToken ct);
    [SqlQuery("Pipeline/ListSteps.sql")]
    public static partial Task<List<StepRow>> ListStepsAsync(this IReadSession db, Guid runId, CancellationToken ct);
    [SqlQuery("Pipeline/UpdateStep.sql")]
    public static partial Task UpdateStepAsync(this IWriteSession db, Guid runId, string stepId, string state, int attempt, Guid? refId, string output,
        string? error, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/ResetStep.sql")]
    public static partial Task ResetStepAsync(this IWriteSession db, Guid runId, string stepId, CancellationToken ct);
}

/// <summary>
/// Pipeline definition (M14 §2): `{"params":{…defaults}, "steps":[{"id","kind","needs":[…],"config":{…},"retries":0}]}`.
/// Steps: `dataset` (import from Hugging Face / URL / S3 into a dataset version + its classes), `train` (M09 run), `job` (M03 job),
/// `gate` (condition), `register` (M11 from run), `promote` (change stage).
/// Strings in config may reference `${params.x}` / `${steps.&lt;id&gt;.&lt;field&gt;}` (when the whole string is one reference → the original type is kept).
/// </summary>
public static partial class PipelineDef
{
    public sealed record Step(string Id, string Kind, string[] Needs, JsonObject Config, int Retries);

    public static readonly string[] Kinds = ["dataset", "train", "job", "gate", "register", "promote"];

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,40}$")]
    private static partial Regex StepId();

    [GeneratedRegex(@"\$\{(params|steps)\.([A-Za-z0-9_-]+)(?:\.([A-Za-z0-9_]+))?\}")]
    private static partial Regex Ref();

    private static ValidationException Bad(string msg) => new(new Dictionary<string, string> { ["definition"] = msg });

    public static (JsonObject Params, List<Step> Steps) Parse(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw Bad("Definition must be an object"); }
        catch (JsonException) { throw Bad("Invalid JSON"); }
        var steps = new List<Step>();
        foreach (var n in root["steps"] as JsonArray ?? throw Bad("steps is required"))
        {
            if (n is not JsonObject o) throw Bad("Each step must be an object");
            var id = o["id"]?.ToString() ?? "";
            if (!StepId().IsMatch(id)) throw Bad($"Invalid step id: '{id}'");
            if (steps.Any(s => s.Id == id)) throw Bad($"Duplicate step id {id}");
            var kind = o["kind"]?.ToString() ?? "";
            if (!Kinds.Contains(kind)) throw Bad($"Step {id}: kind '{kind}' is not supported ({string.Join(", ", Kinds)})");
            var needs = (o["needs"] as JsonArray)?.Select(x => x?.ToString() ?? "").ToArray() ?? [];
            var config = o["config"] as JsonObject ?? [];
            Require(id, kind, config);
            steps.Add(new Step(id, kind, needs, (JsonObject)config.DeepClone(), Math.Clamp(o["retries"]?.GetValue<int>() ?? 0, 0, 5)));
        }
        if (steps.Count == 0) throw Bad("At least one step is required");
        foreach (var s in steps)
            foreach (var need in s.Needs)
                if (steps.All(x => x.Id != need)) throw Bad($"Step {s.Id} needs '{need}', which does not exist");
        // Cycle check (Kahn)
        var indeg = steps.ToDictionary(s => s.Id, s => s.Needs.Length);
        var queue = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var seen = 0;
        while (queue.Count > 0)
        {
            var id = queue.Dequeue(); seen++;
            foreach (var s in steps.Where(s => s.Needs.Contains(id)))
                if (--indeg[s.Id] == 0) queue.Enqueue(s.Id);
        }
        if (seen != steps.Count) throw Bad("The graph has a cycle");
        foreach (var s in steps)
            foreach (Match m in Ref().Matches(s.Config.ToJsonString()))
                if (m.Groups[1].Value == "steps" && !Upstream(steps, s).Contains(m.Groups[2].Value))
                    throw Bad($"Step {s.Id} references ${{steps.{m.Groups[2].Value}}} but does not depend on that step");
        return ((root["params"] as JsonObject)?.DeepClone() as JsonObject ?? [], steps);
    }

    private static HashSet<string> Upstream(List<Step> steps, Step s)
    {
        var set = new HashSet<string>();
        var stack = new Stack<string>(s.Needs);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!set.Add(id)) continue;
            foreach (var n in steps.First(x => x.Id == id).Needs) stack.Push(n);
        }
        return set;
    }

    private static void Require(string id, string kind, JsonObject c)
    {
        string[] need = kind switch
        {
            "dataset" => ["repo", "name", "task"],
            "train" => ["recipe", "variant"],
            "job" => ["image", "command"],
            "gate" => ["left", "op", "right"],
            "register" => ["run_id", "model_name"],
            "promote" => ["version_id", "stage"],
            _ => [],
        };
        var missing = need.Where(k => c[k] is null).ToList();
        if (missing.Count > 0) throw Bad($"Step {id} ({kind}) is missing config: {string.Join(", ", missing)}");
        if (kind == "train" && c["project_id"] is null && c["project_name"] is null) throw Bad($"Step {id} (train) needs project_id or project_name");
        if (kind == "gate" && c["op"]?.ToString() is not (">" or ">=" or "<" or "<=" or "==" or "!="))
            throw Bad($"Step {id}: op must be one of > >= < <= == !=");
    }

    /// <summary>Replaces references in config with parameter values / outputs of earlier steps.</summary>
    public static JsonNode? Resolve(JsonNode? node, JsonObject runParams, IReadOnlyDictionary<string, JsonObject> outputs)
    {
        switch (node)
        {
            case JsonObject o:
                var r = new JsonObject();
                foreach (var (k, v) in o) r[k] = Resolve(v, runParams, outputs);
                return r;
            case JsonArray a:
                return new JsonArray(a.Select(x => Resolve(x, runParams, outputs)).ToArray());
            case JsonValue v when v.TryGetValue<string>(out var s) && s.Contains("${"):
                var whole = Ref().Match(s);
                if (whole.Success && whole.Length == s.Length) return Lookup(whole, runParams, outputs)?.DeepClone();
                return JsonValue.Create(Ref().Replace(s, m => Lookup(m, runParams, outputs) switch
                {
                    JsonValue jv when jv.TryGetValue<string>(out var str) => str,
                    null => "",
                    var other => other.ToJsonString(),
                }));
            default:
                return node?.DeepClone();
        }
    }

    private static JsonNode? Lookup(Match m, JsonObject runParams, IReadOnlyDictionary<string, JsonObject> outputs) =>
        m.Groups[1].Value == "params" ? runParams[m.Groups[2].Value]
        : outputs.TryGetValue(m.Groups[2].Value, out var o) ? o[m.Groups[3].Success ? m.Groups[3].Value : "result"] : null;

    public static async Task<Guid> CreateRunAsync(IWriteSession db, IClock clock, PipelineRow p, JsonObject? overrides, string trigger, Guid createdBy, CancellationToken ct)
    {
        var (defaults, steps) = Parse(p.Definition);
        foreach (var (k, v) in overrides ?? []) defaults[k] = v?.DeepClone();
        var id = Ids.New();
        await db.InsertRunAsync(id, p.Id, p.WorkspaceId, defaults.ToJsonString(), trigger, createdBy, clock.UtcNow, ct);
        foreach (var s in steps) await db.InsertStepAsync(id, s.Id, s.Kind, ct);
        return id;
    }
}

// ───────────── RPC ─────────────

public sealed record CreatePipelineCommand(Guid WorkspaceId, string Name, string DefinitionJson, string TriggerJson) : ICommand<PipelineRow>;

public sealed class CreatePipelineCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreatePipelineCommand, PipelineRow>
{
    public async ValueTask<PipelineRow> Handle(CreatePipelineCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        if (string.IsNullOrWhiteSpace(c.Name)) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Pipeline name is required" });
        _ = PipelineDef.Parse(c.DefinitionJson);
        var trigger = ParseTrigger(c.TriggerJson);
        var id = Ids.New();
        try { await db.InsertPipelineAsync(id, c.WorkspaceId, c.Name.Trim(), c.DefinitionJson, trigger.ToJsonString(), uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("PIPELINE_NAME_TAKEN", "Pipeline name is already in use"); }
        return (await db.GetPipelineAsync(id, c.WorkspaceId, ct))!;
    }

    /// <summary>manual (default) | interval {minutes ≥ 5} | event {event: deployment.drift | …}.</summary>
    private static JsonObject ParseTrigger(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject { ["type"] = "manual" };
        JsonObject t;
        try { t = JsonNode.Parse(json) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new ValidationException(new Dictionary<string, string> { ["trigger"] = "Invalid JSON" }); }
        var ok = t["type"]?.ToString() switch
        {
            "manual" => true,
            "interval" => t["minutes"] is JsonValue m && m.TryGetValue<int>(out var mins) && mins >= 5,
            "event" => t["event"]?.ToString() is "deployment.drift",
            _ => false,
        };
        if (!ok) throw new InvalidArgumentException("PIPELINE_TRIGGER_INVALID", "Trigger: manual | interval {minutes ≥ 5} | event {event: deployment.drift}");
        return t;
    }
}

public sealed record ListPipelinesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<PipelineRow>>;

public sealed class ListPipelinesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPipelinesQuery, IReadOnlyList<PipelineRow>>
{
    public async ValueTask<IReadOnlyList<PipelineRow>> Handle(ListPipelinesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListPipelinesAsync(q.WorkspaceId, ct);
    }
}

public sealed record StartPipelineRunCommand(Guid WorkspaceId, Guid PipelineId, string ParamsJson) : ICommand<PipelineRunRow>;

public sealed class StartPipelineRunCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<StartPipelineRunCommand, PipelineRunRow>
{
    public async ValueTask<PipelineRunRow> Handle(StartPipelineRunCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var p = await db.GetPipelineAsync(c.PipelineId, c.WorkspaceId, ct) ?? throw new NotFoundException("PIPELINE_NOT_FOUND", "Pipeline not found");
        var id = await PipelineDef.CreateRunAsync(db, clock, p, PipelineRuns.ParamsOrThrow(c.ParamsJson), "manual", uid, ct);
        return (await db.GetPipelineRunAsync(id, c.WorkspaceId, ct))!;
    }
}

internal static class PipelineRuns
{
    public static JsonObject? ParamsOrThrow(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new ValidationException(new Dictionary<string, string> { ["params"] = "Invalid JSON" }); }
    }
}

public sealed record ListPipelineRunsQuery(Guid WorkspaceId, Guid? PipelineId) : IQuery<IReadOnlyList<PipelineRunRow>>;

public sealed class ListPipelineRunsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPipelineRunsQuery, IReadOnlyList<PipelineRunRow>>
{
    public async ValueTask<IReadOnlyList<PipelineRunRow>> Handle(ListPipelineRunsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListPipelineRunsAsync(q.WorkspaceId, q.PipelineId, ct);
    }
}

public sealed record GetPipelineRunQuery(Guid WorkspaceId, Guid RunId) : IQuery<(PipelineRunRow Run, IReadOnlyList<StepRow> Steps, string Definition)>;

public sealed class GetPipelineRunQueryHandler(IReadSession db, WorkspaceAuthorizer auth)
    : IQueryHandler<GetPipelineRunQuery, (PipelineRunRow Run, IReadOnlyList<StepRow> Steps, string Definition)>
{
    public async ValueTask<(PipelineRunRow Run, IReadOnlyList<StepRow> Steps, string Definition)> Handle(GetPipelineRunQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var r = await db.GetPipelineRunAsync(q.RunId, q.WorkspaceId, ct) ?? throw new NotFoundException("PIPELINE_RUN_NOT_FOUND", "Pipeline run not found");
        var p = await db.GetPipelineAsync(r.PipelineId, q.WorkspaceId, ct);
        return (r, await db.ListStepsAsync(r.Id, ct), p?.Definition ?? "{}");
    }
}

/// <summary>Rerun from the failed step (M14 §1): keeps outputs of succeeded steps, resets failed + skipped steps; parameters may be changed.</summary>
public sealed record RetryPipelineRunCommand(Guid WorkspaceId, Guid RunId, string ParamsJson) : ICommand<PipelineRunRow>;

public sealed class RetryPipelineRunCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<RetryPipelineRunCommand, PipelineRunRow>
{
    public async ValueTask<PipelineRunRow> Handle(RetryPipelineRunCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var r = await db.GetPipelineRunAsync(c.RunId, c.WorkspaceId, ct) ?? throw new NotFoundException("PIPELINE_RUN_NOT_FOUND", "Pipeline run not found");
        if (r.State is not ("failed" or "cancelled")) throw new PreconditionException("PIPELINE_RUN_NOT_FAILED", "Only failed or cancelled runs can be rerun");
        var p = JsonNode.Parse(r.Params) as JsonObject ?? [];
        foreach (var (k, v) in PipelineRuns.ParamsOrThrow(c.ParamsJson) ?? []) p[k] = v?.DeepClone();
        foreach (var s in await db.ListStepsAsync(r.Id, ct))
            if (s.State is "failed" or "skipped" or "running" or "pending") await db.ResetStepAsync(r.Id, s.StepId, ct);
        await db.ReopenPipelineRunAsync(r.Id, p.ToJsonString(), ct);
        return (await db.GetPipelineRunAsync(r.Id, c.WorkspaceId, ct))!;
    }
}

/// <summary>Cancels a run; returns running jobs so the host stops the containers.</summary>
public sealed record CancelPipelineRunCommand(Guid WorkspaceId, Guid RunId) : ICommand<(PipelineRunRow Run, IReadOnlyList<Guid> Jobs)>;

public sealed class CancelPipelineRunCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator)
    : ICommandHandler<CancelPipelineRunCommand, (PipelineRunRow Run, IReadOnlyList<Guid> Jobs)>
{
    public async ValueTask<(PipelineRunRow Run, IReadOnlyList<Guid> Jobs)> Handle(CancelPipelineRunCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var r = await db.GetPipelineRunAsync(c.RunId, c.WorkspaceId, ct) ?? throw new NotFoundException("PIPELINE_RUN_NOT_FOUND", "Pipeline run not found");
        var jobs = new List<Guid>();
        foreach (var s in (await db.ListStepsAsync(r.Id, ct)).Where(s => s.State == "running" && s.RefId is not null))
        {
            if (s.Kind == "train") { if ((await mediator.Query(new GetRunQuery(c.WorkspaceId, s.RefId!.Value), ct)).JobId is { } j) jobs.Add(j); }
            else if (s.Kind == "job") jobs.Add(s.RefId!.Value);
            await db.UpdateStepAsync(r.Id, s.StepId, "failed", s.Attempt, s.RefId, s.Output, "Cancelled", clock.UtcNow, ct);
        }
        await db.FinishPipelineRunAsync(r.Id, "cancelled", "Cancelled by user", clock.UtcNow, ct);
        return ((await db.GetPipelineRunAsync(r.Id, c.WorkspaceId, ct))!, jobs);
    }
}

// ───────────── Engine ─────────────

/// <summary>
/// One engine tick for a run (M14 §5): checks running steps (M09 run / M03 job), starts eligible steps, skips branches after a failed step/closed gate,
/// finalizes the run when every step has finished. The host dispatches it in a scope acting as the user who started the run (child commands are audited as that user).
/// </summary>
public sealed record AdvancePipelineRunCommand(Guid WorkspaceId, Guid RunId) : ICommand<bool>, IInternalCommand;

public sealed class AdvancePipelineRunCommandHandler(IWriteSession db, IClock clock, IMediator mediator, IHubImportQueue imports) : ICommandHandler<AdvancePipelineRunCommand, bool>
{
    public async ValueTask<bool> Handle(AdvancePipelineRunCommand c, CancellationToken ct)
    {
        var run = await db.GetPipelineRunAsync(c.RunId, c.WorkspaceId, ct);
        if (run is null || run.State != "running") return false;
        var pipeline = (await db.GetPipelineAsync(run.PipelineId, run.WorkspaceId, ct))!;
        var (_, defs) = PipelineDef.Parse(pipeline.Definition);
        var runParams = JsonNode.Parse(run.Params) as JsonObject ?? [];
        var changed = true;
        // Steps started in this tick are not committed yet → read queries (another connection) cannot see the run/job; only check from the next tick.
        var startedNow = new HashSet<string>();
        RetriedNow = startedNow;
        for (var guard = 0; changed && guard < defs.Count + 2; guard++)
        {
            changed = false;
            var steps = (await db.ListStepsAsync(run.Id, ct)).ToDictionary(s => s.StepId);
            var outputs = steps.Where(kv => kv.Value.State == "succeeded").ToDictionary(kv => kv.Key, kv => JsonNode.Parse(kv.Value.Output) as JsonObject ?? []);
            foreach (var def in defs)
            {
                var s = steps[def.Id];
                if (s.State == "running") { if (!startedNow.Contains(def.Id)) changed |= await PollAsync(run, def, s, ct); continue; }
                if (s.State != "pending") continue;
                var needs = def.Needs.Select(n => steps[n].State).ToList();
                if (needs.Any(st => st is "failed" or "skipped" or "gated"))
                {
                    await db.UpdateStepAsync(run.Id, def.Id, "skipped", 0, null, "{}", "Previous step did not succeed / gate closed", clock.UtcNow, ct);
                    changed = true;
                }
                else if (needs.All(st => st == "succeeded"))
                {
                    await StartAsync(run, def, runParams, outputs, 1, ct);
                    startedNow.Add(def.Id);
                    changed = true;
                }
            }
        }
        var final = await db.ListStepsAsync(run.Id, ct);
        if (final.All(s => s.State is "succeeded" or "failed" or "skipped" or "gated"))
        {
            var state = final.Any(s => s.State == "failed") ? "failed" : final.Any(s => s.State == "gated") ? "gated" : "succeeded";
            var err = final.FirstOrDefault(s => s.State == "failed") is { } f ? $"{f.StepId}: {f.Error}" : null;
            await db.FinishPipelineRunAsync(run.Id, state, err, clock.UtcNow, ct);
            var ok = state != "failed";
            await Notifier.SendAsync(db, clock, run.WorkspaceId, [run.CreatedBy], ok ? "pipeline.succeeded" : "pipeline.failed",
                $"Pipeline \"{run.PipelineName}\" #{run.Number} {(state switch { "succeeded" => "finished", "gated" => "stopped at gate", _ => "failed" })}",
                $"#/pipelines/runs/{run.Id}", $"pipeline/{run.PipelineId}", ok ? "success" : "error", ct);
        }
        return true;
    }

    private async Task StartAsync(PipelineRunRow run, PipelineDef.Step def, JsonObject runParams, IReadOnlyDictionary<string, JsonObject> outputs, int attempt, CancellationToken ct)
    {
        var now = clock.UtcNow;
        JsonObject cfg;
        try { cfg = (JsonObject)PipelineDef.Resolve(def.Config, runParams, outputs)!; }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            await db.UpdateStepAsync(run.Id, def.Id, "failed", attempt, null, "{}", "Could not resolve reference: " + e.Message, now, ct);
            return;
        }
        try
        {
            switch (def.Kind)
            {
                case "dataset":
                {
                    var (dsId, versionId, importId, note) = await StartDatasetAsync(run, cfg, ct);
                    var output = new JsonObject { ["dataset_id"] = dsId.ToString(), ["note"] = note };
                    if (versionId is { } vid)
                    {
                        output["dataset_version_id"] = vid.ToString();
                        output["result"] = vid.ToString();
                        await db.UpdateStepAsync(run.Id, def.Id, "succeeded", attempt, vid, output.ToJsonString(), null, now, ct);
                    }
                    else await db.UpdateStepAsync(run.Id, def.Id, "running", attempt, importId, output.ToJsonString(), null, now, ct);
                    break;
                }
                case "train":
                {
                    var projectId = cfg["project_id"] is not null ? G(cfg, "project_id") : await ProjectAsync(run.WorkspaceId, S(cfg, "project_name"), cfg["task"]?.ToString() ?? "", ct);
                    var r = await mediator.Send(new StartTrainingCommand(run.WorkspaceId, projectId, cfg["name"]?.ToString() ?? $"{run.PipelineName}-{run.Number}-{def.Id}",
                        S(cfg, "recipe"), S(cfg, "variant"), cfg["task"]?.ToString() ?? "", ParamsJson(cfg["params"]), cfg["from_scratch"] is JsonValue fs && fs.TryGetValue<bool>(out var fsb) && fsb,
                        cfg["cpus"]?.GetValue<double>() ?? 1, cfg["gpus"]?.GetValue<int>() ?? 0, cfg["image_override"]?.ToString(), cfg["network"]?.ToString(),
                        cfg["dataset_version_id"] is { } dv && Guid.TryParse(dv.ToString(), out var dvId) ? dvId : null), ct);
                    await db.UpdateStepAsync(run.Id, def.Id, "running", attempt, r.Id, new JsonObject { ["run_id"] = r.Id.ToString(), ["job_id"] = r.JobId?.ToString(), ["project_id"] = r.ProjectId.ToString() }.ToJsonString(), null, now, ct);
                    break;
                }
                case "job":
                {
                    var spec = new JobSpecDto
                    {
                        Image = S(cfg, "image"), Command = (cfg["command"] as JsonArray ?? []).Select(x => x!.ToString()).ToList(),
                        Cpus = cfg["cpus"]?.GetValue<double>() ?? 1, Network = cfg["network"]?.ToString(),
                    };
                    var job = await mediator.Send(new SubmitJobCommand(run.WorkspaceId, "pipeline", $"{run.PipelineName}-{run.Number}-{def.Id}", spec), ct);
                    await db.UpdateStepAsync(run.Id, def.Id, "running", attempt, job, new JsonObject { ["job_id"] = job.ToString() }.ToJsonString(), null, now, ct);
                    break;
                }
                case "gate":
                {
                    var left = Num(cfg["left"]);
                    var right = Num(cfg["right"]);
                    var pass = cfg["op"]!.ToString() switch
                    {
                        ">" => left > right, ">=" => left >= right, "<" => left < right, "<=" => left <= right, "==" => left == right, _ => left != right,
                    };
                    var output = new JsonObject { ["passed"] = pass, ["left"] = left, ["right"] = right, ["result"] = pass };
                    await db.UpdateStepAsync(run.Id, def.Id, pass ? "succeeded" : "gated", attempt, null, output.ToJsonString(),
                        pass ? null : $"{left.ToString(CultureInfo.InvariantCulture)} {cfg["op"]} {right.ToString(CultureInfo.InvariantCulture)} not satisfied", now, ct);
                    break;
                }
                case "register":
                {
                    var v = await mediator.Send(new RegisterFromRunCommand(run.WorkspaceId, G(cfg, "run_id"), S(cfg, "model_name"),
                        cfg["description"]?.ToString() ?? $"Pipeline {run.PipelineName} #{run.Number}"), ct);
                    await db.UpdateStepAsync(run.Id, def.Id, "succeeded", attempt, v.Id, new JsonObject
                        { ["version_id"] = v.Id.ToString(), ["version"] = v.Version, ["model"] = v.ModelName, ["result"] = v.Id.ToString() }.ToJsonString(), null, now, ct);
                    break;
                }
                case "promote":
                {
                    var v = await mediator.Send(new TransitionStageCommand(run.WorkspaceId, G(cfg, "version_id"), S(cfg, "stage"),
                        cfg["reason"]?.ToString() ?? $"Pipeline {run.PipelineName} #{run.Number}"), ct);
                    await db.UpdateStepAsync(run.Id, def.Id, "succeeded", attempt, v.Id, new JsonObject
                        { ["version_id"] = v.Id.ToString(), ["stage"] = v.Stage, ["result"] = v.Stage }.ToJsonString(), null, now, ct);
                    break;
                }
            }
        }
        catch (AppException e)
        {
            await db.UpdateStepAsync(run.Id, def.Id, "failed", attempt, null, "{}", $"{e.Reason}: {e.Message}", now, ct);
        }
    }

    /// <summary>Asynchronous step: tracks the M09 run / M03 job; failed with retries left → rerun the step.</summary>
    private async Task<bool> PollAsync(PipelineRunRow run, PipelineDef.Step def, StepRow s, CancellationToken ct)
    {
        if (s.RefId is not { } refId) return false;
        string state;
        var output = JsonNode.Parse(s.Output) as JsonObject ?? [];
        if (def.Kind == "dataset")
        {
            var imp = (await mediator.Query(new ListHubImportsQuery(run.WorkspaceId), ct)).FirstOrDefault(x => x.Id == refId);
            state = imp?.State switch { "succeeded" => "succeeded", "failed" or null => "failed", _ => "running" };
            if (imp?.State == "queued") imports.Enqueue(refId);   // the worker lost it (api restart) → hand it over again; a started import is not run twice
            output["progress"] = imp is null ? "" : $"{imp.FilesDone}/{imp.FilesTotal} files · {imp.Message}";
            if (state == "succeeded" && imp!.TargetId is { } dsId)
            {
                var latest = (await db.ListVersionsAsync(dsId, ct)).OrderByDescending(v => v.Number).FirstOrDefault();
                if (latest is null) state = "failed";
                else { output["dataset_version_id"] = latest.Id.ToString(); output["result"] = latest.Id.ToString(); }
            }
            if (state == "failed") output["error"] = imp?.Message ?? "Import not found";
        }
        else if (def.Kind == "train")
        {
            var r = await mediator.Query(new GetRunQuery(run.WorkspaceId, refId), ct);
            state = r.State;
            output["metric"] = r.PrimaryMetricValue;
            output["metric_key"] = r.PrimaryMetricKey;
            output["result"] = r.Id.ToString();
        }
        else
        {
            var j = await mediator.Query(new GetJobQuery(run.WorkspaceId, refId), ct);
            state = j.State;
            output["result"] = j.State;
        }
        if (state == "succeeded")
        {
            await db.UpdateStepAsync(run.Id, def.Id, "succeeded", s.Attempt, refId, output.ToJsonString(), null, clock.UtcNow, ct);
            return true;
        }
        if (state is not ("failed" or "cancelled")) return false;
        if (s.Attempt <= def.Retries)
        {
            var runParams = JsonNode.Parse(run.Params) as JsonObject ?? [];
            var outputs = (await db.ListStepsAsync(run.Id, ct)).Where(x => x.State == "succeeded")
                .ToDictionary(x => x.StepId, x => JsonNode.Parse(x.Output) as JsonObject ?? []);
            await StartAsync(run, def, runParams, outputs, s.Attempt + 1, ct);
            RetriedNow?.Add(def.Id);
            return true;
        }
        await db.UpdateStepAsync(run.Id, def.Id, "failed", s.Attempt, refId, output.ToJsonString(),
            def.Kind == "dataset" ? $"Import failed: {output["error"]}" : $"{(def.Kind == "train" ? "Run" : "Job")} {state}", clock.UtcNow, ct);
        return true;
    }

    private HashSet<string>? RetriedNow;

    /// <summary>
    /// `dataset` step: the dataset named in config (slug from the name) is reused when it already has a version (reuse: false → import again);
    /// otherwise an import starts (default: Hugging Face, reference mode — files stay at the pinned source commit). `classes` (names, index = class id)
    /// becomes the dataset's label schema when it differs, so training gets {dataset.classes.*} and num_classes from it.
    /// </summary>
    private async Task<(Guid DatasetId, Guid? VersionId, Guid? ImportId, string Note)> StartDatasetAsync(PipelineRunRow run, JsonObject cfg, CancellationToken ct)
    {
        var name = S(cfg, "name");
        var task = S(cfg, "task");
        var slug = new string(name.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        var existing = (await db.ListDatasetsAsync(run.WorkspaceId, task, ct)).FirstOrDefault(d => d.Slug == slug || d.Slug == "import-" + slug);
        var dsId = existing?.Id;
        if (dsId is null)
            dsId = (await mediator.Send(new CreateDatasetCommand(run.WorkspaceId, slug.Length >= 2 ? slug : "import-" + slug, name, task), ct)).Id;
        if (cfg["classes"] is JsonArray classes && classes.Count > 0)
        {
            var wanted = new JsonArray(classes.Select((c, i) => (JsonNode)new JsonObject { ["id"] = i, ["name"] = c?.ToString() }).ToArray()).ToJsonString();
            var current = await db.GetCurrentSchemaAsync(dsId.Value, ct);
            if (current is null || !SameClasses(current.Classes, wanted))
                await mediator.Send(new CreateLabelSchemaCommand(run.WorkspaceId, dsId.Value, task, wanted), ct);
        }
        var reuse = cfg["reuse"] is not JsonValue rv || !rv.TryGetValue<bool>(out var rb) || rb;
        if (existing is not null && reuse && (await db.ListVersionsAsync(dsId.Value, ct)).OrderByDescending(v => v.Number).FirstOrDefault() is { } latest)
            return (dsId.Value, latest.Id, null, $"Reused dataset {existing.Name} v{latest.Number}");
        var include = (cfg["include"] as JsonArray)?.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToArray() ?? [];
        var importId = await mediator.Send(new StartHubImportCommand(run.WorkspaceId, "dataset", S(cfg, "repo"), cfg["revision"]?.ToString() ?? "main", include, dsId,
            name, task, cfg["source"]?.ToString() ?? "hf", cfg["mode"]?.ToString() ?? "reference"), ct);
        imports.Enqueue(importId);
        return (dsId.Value, null, importId, $"Importing {S(cfg, "repo")}");
    }

    private static bool SameClasses(string a, string b)
    {
        static string Key(string json) => string.Join("|", (JsonNode.Parse(json) as JsonArray ?? []).Select(n => $"{n?["id"]}={n?["name"]}"));
        return Key(a) == Key(b);
    }

    /// <summary>`train` step with project_name: the project with that slug, created (with the step's task) on first use.</summary>
    private async Task<Guid> ProjectAsync(Guid workspaceId, string name, string task, CancellationToken ct)
    {
        var slug = new string(name.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if ((await mediator.Query(new ListProjectsQuery(workspaceId), ct)).FirstOrDefault(p => p.Slug == slug) is { } p) return p.Id;
        if (task.Length == 0) throw new InvalidArgumentException("PIPELINE_TASK_REQUIRED", "A train step that creates its project needs a task");
        return (await mediator.Send(new CreateProjectCommand(workspaceId, slug, name, task), ct)).Id;
    }

    /// <summary>Recipe params after reference resolution; numeric strings from run params become numbers ("24" → 24).</summary>
    private static string ParamsJson(JsonNode? node)
    {
        var o = node as JsonObject ?? [];
        var r = new JsonObject();
        foreach (var (k, v) in o)
            r[k] = v is JsonValue jv && jv.TryGetValue<string>(out var str) && double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? (d == Math.Floor(d) && Math.Abs(d) < int.MaxValue ? JsonValue.Create((int)d) : JsonValue.Create(d)) : v?.DeepClone();
        return r.ToJsonString();
    }

    private static string S(JsonObject c, string k) => c[k]?.ToString() ?? "";

    private static Guid G(JsonObject c, string k) =>
        Guid.TryParse(c[k]?.ToString(), out var g) ? g : throw new InvalidArgumentException("PIPELINE_REF_INVALID", $"{k} is not a UUID (reference has no value yet?)");

    private static double Num(JsonNode? n) =>
        n is JsonValue v && (v.TryGetValue<double>(out var d) || (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)))
            ? d : throw new InvalidArgumentException("PIPELINE_GATE_NOT_NUMBER", "Gate requires a numeric value (metric not available yet?)");
}

public sealed record ListActivePipelineRunsQuery : IQuery<IReadOnlyList<PipelineRunRow>>;

public sealed class ListActivePipelineRunsQueryHandler(IReadSession db) : IQueryHandler<ListActivePipelineRunsQuery, IReadOnlyList<PipelineRunRow>>
{
    public async ValueTask<IReadOnlyList<PipelineRunRow>> Handle(ListActivePipelineRunsQuery q, CancellationToken ct) => await db.ListActivePipelineRunsAsync(ct);
}

/// <summary>Schedule triggers (called from the background loop) and event triggers (M13 drift…): create a run on behalf of the pipeline creator.</summary>
public static class PipelineTriggers
{
    public static async Task<int> FireAsync(IWriteSession db, IClock clock, string eventKind, Guid workspaceId, JsonObject? eventParams, CancellationToken ct)
    {
        var n = 0;
        foreach (var p in await db.ListTriggeredAsync(clock.UtcNow, eventKind, workspaceId, ct))
        {
            var isEvent = eventKind != "" && p.Trigger.Contains("\"event\"");
            await PipelineDef.CreateRunAsync(db, clock, p, isEvent ? eventParams : null, isEvent ? "event:" + eventKind : "interval", p.CreatedBy, ct);
            await db.MarkTriggeredAsync(p.Id, clock.UtcNow, ct);
            n++;
        }
        return n;
    }
}

public sealed record FireIntervalTriggersCommand : ICommand<int>, IInternalCommand;

public sealed class FireIntervalTriggersCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<FireIntervalTriggersCommand, int>
{
    public async ValueTask<int> Handle(FireIntervalTriggersCommand c, CancellationToken ct) => await PipelineTriggers.FireAsync(db, clock, "", Guid.Empty, null, ct);
}
