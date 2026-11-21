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

// ───────────── Engine ─────────────

/// <summary>One engine tick for a run (M14 §5): checks running steps (M09 run / M03 job), starts eligible steps, skips
/// branches after a failed step/closed gate, finalizes the run when every step has finished.</summary>
[Access(AccessKind.Internal)]
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
        await FinishIfDoneAsync(run, ct);
        return true;
    }

    private async Task FinishIfDoneAsync(PipelineRunRow run, CancellationToken ct)
    {
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
                case "dataset": await StartDatasetStepAsync(run, def, cfg, attempt, now, ct); break;
                case "train": await StartTrainStepAsync(run, def, cfg, attempt, now, ct); break;
                case "job": await StartJobStepAsync(run, def, cfg, attempt, now, ct); break;
                case "gate": await EvaluateGateStepAsync(run, def, cfg, attempt, now, ct); break;
                case "register": await RegisterStepAsync(run, def, cfg, attempt, now, ct); break;
                case "promote": await PromoteStepAsync(run, def, cfg, attempt, now, ct); break;
            }
        }
        catch (AppException e)
        {
            await db.UpdateStepAsync(run.Id, def.Id, "failed", attempt, null, "{}", $"{e.Reason}: {e.Message}", now, ct);
        }
    }

    private async Task StartDatasetStepAsync(PipelineRunRow run, PipelineDef.Step def, JsonObject cfg, int attempt, DateTimeOffset now, CancellationToken ct)
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
    }

    private async Task StartTrainStepAsync(PipelineRunRow run, PipelineDef.Step def, JsonObject cfg, int attempt, DateTimeOffset now, CancellationToken ct)
    {
        var projectId = cfg["project_id"] is not null ? G(cfg, "project_id") : await ProjectAsync(run.WorkspaceId, S(cfg, "project_name"), cfg["task"]?.ToString() ?? "", ct);
        var r = await mediator.Send(new StartTrainingCommand(run.WorkspaceId, projectId, cfg["name"]?.ToString() ?? $"{run.PipelineName}-{run.Number}-{def.Id}",
            S(cfg, "recipe"), S(cfg, "variant"), cfg["task"]?.ToString() ?? "", ParamsJson(cfg["params"]), cfg["from_scratch"] is JsonValue fs && fs.TryGetValue<bool>(out var fsb) && fsb,
            cfg["cpus"]?.GetValue<double>() ?? 1, cfg["gpus"]?.GetValue<int>() ?? 0, cfg["image_override"]?.ToString(), cfg["network"]?.ToString(),
            cfg["dataset_version_id"] is { } dv && Guid.TryParse(dv.ToString(), out var dvId) ? dvId : null), ct);
        await db.UpdateStepAsync(run.Id, def.Id, "running", attempt, r.Id, new JsonObject { ["run_id"] = r.Id.ToString(), ["job_id"] = r.JobId?.ToString(), ["project_id"] = r.ProjectId.ToString() }.ToJsonString(), null, now, ct);
    }

    private async Task StartJobStepAsync(PipelineRunRow run, PipelineDef.Step def, JsonObject cfg, int attempt, DateTimeOffset now, CancellationToken ct)
    {
        var spec = new JobSpecDto
        {
            Image = S(cfg, "image"), Command = (cfg["command"] as JsonArray ?? []).Select(x => x!.ToString()).ToList(),
            Cpus = cfg["cpus"]?.GetValue<double>() ?? 1, Network = cfg["network"]?.ToString(),
        };
        var job = await mediator.Send(new SubmitJobCommand(run.WorkspaceId, "pipeline", $"{run.PipelineName}-{run.Number}-{def.Id}", spec), ct);
        await db.UpdateStepAsync(run.Id, def.Id, "running", attempt, job, new JsonObject { ["job_id"] = job.ToString() }.ToJsonString(), null, now, ct);
    }

    private async Task EvaluateGateStepAsync(PipelineRunRow run, PipelineDef.Step def, JsonObject cfg, int attempt, DateTimeOffset now, CancellationToken ct)
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
    }

    private async Task RegisterStepAsync(PipelineRunRow run, PipelineDef.Step def, JsonObject cfg, int attempt, DateTimeOffset now, CancellationToken ct)
    {
        var v = await mediator.Send(new RegisterFromRunCommand(run.WorkspaceId, G(cfg, "run_id"), S(cfg, "model_name"),
            cfg["description"]?.ToString() ?? $"Pipeline {run.PipelineName} #{run.Number}"), ct);
        await db.UpdateStepAsync(run.Id, def.Id, "succeeded", attempt, v.Id, new JsonObject
            { ["version_id"] = v.Id.ToString(), ["version"] = v.Version, ["model"] = v.ModelName, ["result"] = v.Id.ToString() }.ToJsonString(), null, now, ct);
    }

    private async Task PromoteStepAsync(PipelineRunRow run, PipelineDef.Step def, JsonObject cfg, int attempt, DateTimeOffset now, CancellationToken ct)
    {
        var v = await mediator.Send(new TransitionStageCommand(run.WorkspaceId, G(cfg, "version_id"), S(cfg, "stage"),
            cfg["reason"]?.ToString() ?? $"Pipeline {run.PipelineName} #{run.Number}"), ct);
        await db.UpdateStepAsync(run.Id, def.Id, "succeeded", attempt, v.Id, new JsonObject
            { ["version_id"] = v.Id.ToString(), ["stage"] = v.Stage, ["result"] = v.Stage }.ToJsonString(), null, now, ct);
    }

    /// <summary>Asynchronous step: tracks the M09 run / M03 job; failed with retries left → rerun the step.</summary>
    private async Task<bool> PollAsync(PipelineRunRow run, PipelineDef.Step def, StepRow s, CancellationToken ct)
    {
        if (s.RefId is not { } refId) return false;
        var output = JsonNode.Parse(s.Output) as JsonObject ?? [];
        var state = await ReadStepStateAsync(run, def, refId, output, ct);
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

    private async Task<string> ReadStepStateAsync(PipelineRunRow run, PipelineDef.Step def, Guid refId, JsonObject output, CancellationToken ct)
    {
        string state;
        if (def.Kind == "dataset") state = await ReadImportStateAsync(run, refId, output, ct);
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
        return state;
    }

    private async Task<string> ReadImportStateAsync(PipelineRunRow run, Guid refId, JsonObject output, CancellationToken ct)
    {
        var imp = (await mediator.Query(new ListHubImportsQuery(run.WorkspaceId), ct)).FirstOrDefault(x => x.Id == refId);
        var state = imp?.State switch { "succeeded" => "succeeded", "failed" or null => "failed", _ => "running" };
        if (imp?.State == "queued") imports.Enqueue(refId);
        output["progress"] = imp is null ? "" : $"{imp.FilesDone}/{imp.FilesTotal} files · {imp.Message}";
        if (state == "succeeded" && imp!.TargetId is { } dsId)
        {
            var latest = (await db.ListVersionsAsync(dsId, ct)).OrderByDescending(v => v.Number).FirstOrDefault();
            if (latest is null) state = "failed";
            else { output["dataset_version_id"] = latest.Id.ToString(); output["result"] = latest.Id.ToString(); }
        }
        if (state == "failed") output["error"] = imp?.Message ?? "Import not found";
        return state;
    }

    /// <summary>`dataset` step: the dataset named in config (slug from the name) is reused when it already has a
    /// version (reuse: false → import again); otherwise an import starts (default: Hugging Face.</summary>
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

    /// <summary>`train` step with project_name: the project with that slug, created (with the step's task) on first
    /// use.</summary>
    private async Task<Guid> ProjectAsync(Guid workspaceId, string name, string task, CancellationToken ct)
    {
        var slug = new string(name.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if ((await mediator.Query(new ListProjectsQuery(workspaceId), ct)).FirstOrDefault(p => p.Slug == slug) is { } p) return p.Id;
        if (task.Length == 0) throw new InvalidArgumentException("PIPELINE_TASK_REQUIRED", "A train step that creates its project needs a task");
        return (await mediator.Send(new CreateProjectCommand(workspaceId, slug, name, task), ct)).Id;
    }

    /// <summary>Recipe params after reference resolution; numeric strings from run params become numbers ("24" →
    /// 24).</summary>
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
