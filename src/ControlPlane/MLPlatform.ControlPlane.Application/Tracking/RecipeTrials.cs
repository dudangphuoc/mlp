using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>One step of a trial as the shim reported it.</summary>
public sealed record TrialStageView(string Key, string State, double Seconds, string Detail);

/// <summary>What the shim measured during stage 1 (the last "[mlp-trial] {"key":"measure",…}" line).</summary>
public sealed record TrialMeasure(double? StepsPerS, long? PeakVramMb, bool VramEstimated, double? ValSPerBatch, int? TrainImages, int? ValImages);

/// <summary>A trial as the canvas shows it: stored row, live stages while its job runs, the job and the machine.</summary>
public sealed record TrialView(TrialRow Row, string State, IReadOnlyList<TrialStageView> Stages, double? StepsPerS, long? PeakVramMb, bool VramEstimated,
    int TrainImages, int ValImages, JobInfoRow? Job, string? HostName);

/// <summary>Rules shared by the trial handlers (round 1 R15–R22): the data subset, reading the shim's lines, building the view.</summary>
public static class RecipeTrials
{
    public const string Marker = "[mlp-trial] ";
    public const int MinTrainImages = 160;
    public const int ValImages = 128;

    /// <summary>Stages (last state per key, in first-seen order) and the measurement from the shim's <c>[mlp-trial]</c> lines.</summary>
    public static (List<TrialStageView> Stages, TrialMeasure? Measure) Parse(IEnumerable<string> lines)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, TrialStageView>();
        TrialMeasure? measure = null;
        foreach (var line in lines)
        {
            var at = line.IndexOf(Marker, StringComparison.Ordinal);
            if (at < 0) continue;
            JsonObject? o;
            try { o = JsonNode.Parse(line[(at + Marker.Length)..]) as JsonObject; }
            catch (JsonException) { continue; }
            if (o?["key"]?.ToString() is not { Length: > 0 } key) continue;
            if (key == "measure")
            {
                measure = new TrialMeasure(D(o["steps_per_s"]), D(o["peak_vram_mb"]) is { } p ? (long)p : null, o["vram_estimated"]?.GetValueKind() == JsonValueKind.True,
                    D(o["val_s_per_batch"]), D(o["train_images"]) is { } ti ? (int)ti : null, D(o["val_images"]) is { } vi ? (int)vi : null);
                continue;
            }
            if (!byKey.ContainsKey(key)) order.Add(key);
            byKey[key] = new TrialStageView(key, o["state"]?.ToString() ?? "", D(o["seconds"]) ?? 0, o["detail"]?.ToString() ?? "");
        }
        return (order.Select(k => byKey[k]).ToList(), measure);
    }

    private static double? D(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : null;

    public static string StagesJson(IEnumerable<TrialStageView> stages) =>
        new JsonArray(stages.Select(s => (JsonNode)new JsonObject { ["key"] = s.Key, ["state"] = s.State, ["seconds"] = s.Seconds, ["detail"] = s.Detail }).ToArray())
            .ToJsonString();

    public static List<TrialStageView> StagesFrom(string json) =>
        (JsonNode.Parse(string.IsNullOrEmpty(json) ? "[]" : json) as JsonArray ?? []).OfType<JsonObject>()
        .Select(o => new TrialStageView(o["key"]?.ToString() ?? "", o["state"]?.ToString() ?? "", D(o["seconds"]) ?? 0, o["detail"]?.ToString() ?? "")).ToList();

    /// <summary>
    /// The view of a trial. While it waits or runs, <paramref name="liveLines"/> (the attempt's <c>[mlp-trial]</c> lines from the log) give the stages,
    /// and a running job shows the trial as running.
    /// </summary>
    public static async Task<TrialView> ViewAsync(IReadSession db, TrialRow t, IReadOnlyList<string>? liveLines, CancellationToken ct)
    {
        var job = t.JobId is { } j ? (await db.JobsByIdsAsync(t.WorkspaceId, [j], ct)).FirstOrDefault() : null;
        var host = t.HostId is { } h ? (await db.GetHostAsync(h, t.WorkspaceId, ct))?.Name : null;
        var active = t.State is "queued" or "running";
        var stages = StagesFrom(t.Stages);
        var (steps, peak, estimated, train, val) = (t.StepsPerS, t.PeakVramMb, t.VramEstimated, t.TrainImages, t.ValImages);
        if (active && liveLines is { Count: > 0 })
        {
            var (live, m) = Parse(liveLines);
            if (live.Count > 0) stages = live;
            if (m is not null) (steps, peak, estimated) = (m.StepsPerS, m.PeakVramMb, m.VramEstimated);
        }
        var state = active && (job?.State == "running" || stages.Any(s => s.State != "pending")) ? "running" : t.State;
        return new TrialView(t, state, stages, steps, peak, estimated, train, val, job, host);
    }

    /// <summary>
    /// The subset a trial trains on: per split, images in the order of their hash with seed "trial" — train = max(160, 10 × batch), val = 128
    /// (fewer when the split is smaller). The split comes from the version's <c>mlp-split.json</c>, else from train/val folders.
    /// Non-image files are kept when their stem matches a chosen image (labels) or no image at all (classes.txt, data.yaml, the split file).
    /// </summary>
    public static async Task<(HashSet<string> Files, int Train, int Val)> SubsetAsync(IReadSession db, S3Client s3, VersionRow version, string splitState, int batch,
        CancellationToken ct)
    {
        var files = await db.ListVersionFilesAsync(version.Id, ct);
        Dictionary<string, string>? fixedSplit = null;
        if (splitState == "file" && files.FirstOrDefault(f => f.Path == SplitRules.File) is { Url: null }
            && await s3.GetAsync(Buckets.Datasets, version.Prefix + SplitRules.File, ct) is { } body
            && JsonNode.Parse(Encoding.UTF8.GetString(body))?["files"] is JsonObject map)
            fixedSplit = map.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!.ToString());
        // Same rule as the converter: an image missing from the split file, or outside any split folder, counts as train.
        string SplitOf(string p) => fixedSplit is not null ? fixedSplit.GetValueOrDefault(p, "train") : SplitRules.FolderSplit(p) ?? "train";
        var images = files.Where(f => SplitRules.IsImage(f.Path)).Select(f => f.Path).ToList();
        List<string> Pick(string split, int n) =>
            images.Where(p => SplitOf(p) == split).OrderBy(p => SplitRules.Bucket("trial", p)).ThenBy(p => p, StringComparer.Ordinal).Take(n).ToList();
        var train = Pick("train", Math.Max(MinTrainImages, 10 * batch));
        var val = Pick("val", ValImages);
        var chosen = train.Concat(val).ToHashSet(StringComparer.Ordinal);
        static string Stem(string p) => Path.GetFileNameWithoutExtension(p);
        var allStems = images.Select(Stem).ToHashSet(StringComparer.Ordinal);
        var chosenStems = chosen.Select(Stem).ToHashSet(StringComparer.Ordinal);
        foreach (var f in files)
            if (!SplitRules.IsImage(f.Path) && (chosenStems.Contains(Stem(f.Path)) || !allStems.Contains(Stem(f.Path)))) chosen.Add(f.Path);
        return (chosen, train.Count, val.Count);
    }
}

/// <summary>Starts a trial of the caller's draft: freezes it, then trains it hidden on a small subset in two short passes.</summary>
public sealed record StartTrialCommand(Guid WorkspaceId, Guid ProjectRecipeId) : ICommand<TrialView>;

/// <summary>Reuses the training path (job, volume, metrics, log, model upload) with a subset of the data and the recipe's trial overrides.</summary>
public sealed class StartTrialCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator, S3Client s3)
    : ICommandHandler<StartTrialCommand, TrialView>
{
    public async ValueTask<TrialView> Handle(StartTrialCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var state = await mediator.Send(new GetDraftCommand(c.WorkspaceId, c.ProjectRecipeId), ct);
        if (!state.CanTrial) throw new PreconditionException("TRACK_DRAFT_NOT_READY", state.Reasons[0]);
        var (pr, d) = (state.Recipe.Row, state.Draft);
        var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
        if (manifest["entrypoints"]?["train"]?["trial"] is not JsonObject passes)
            throw new PreconditionException("CATALOG_NO_TRIAL", state.Recipe.OriginChanged
                ? "This project's copy of the recipe predates trial runs — take the Catalog version first"
                : $"Recipe {pr.OriginName} does not declare how to run a trial");
        var (rev, trialKey) = await RecipeDrafts.FreezeAsync(db, clock, c.WorkspaceId, uid, pr, d, ct);
        var schema = RecipeDrafts.Schema(manifest);
        var batch = RecipeDrafts.IntValue(RecipeDrafts.Parse(rev.Params), schema, "batch", 1);
        HashSet<string> files = [];
        int train = 0, val = 0;
        if (state.Dataset is { } data)
        {
            (files, train, val) = await RecipeTrials.SubsetAsync(db, s3, data.Version, data.State, batch, ct);
            if (batch > train)
                throw new PreconditionException("TRACK_BATCH_TOO_LARGE", $"Batch {batch} is larger than the {train} train images of the trial — lower the batch");
        }
        var run = await mediator.Send(new StartTrainingCommand(c.WorkspaceId, pr.ProjectId, "", pr.OriginName, rev.Variant, "", rev.Params,
            d.StartKind == "backbone", d.Cpus, d.Gpus, null, null, d.DatasetVersionId, rev.Manifest, d.HostId, new TrialSpec(files, passes)), ct);
        await db.SetRunRevisionAsync(run.Id, c.WorkspaceId, rev.Id, d.TrainKind, d.StartKind, null, d.HostId, ct);
        var id = Ids.New();
        await db.InsertTrialAsync(id, c.WorkspaceId, pr.ProjectId, pr.Id, rev.Id, run.Id, trialKey, d.DatasetVersionId, d.StartKind, d.HostId, d.Gpus, train, val,
            uid, clock.UtcNow, ct);
        return await RecipeTrials.ViewAsync(db, (await db.GetTrialAsync(id, c.WorkspaceId, ct))!, null, ct);
    }
}

/// <summary>
/// One trial. <paramref name="LiveLines"/> returns the <c>[mlp-trial]</c> lines the running attempt of a job has logged so far
/// (the host keeps the log; the application cannot read it itself).
/// </summary>
public sealed record GetTrialQuery(Guid WorkspaceId, Guid TrialId, Func<Guid, Task<IReadOnlyList<string>>>? LiveLines = null) : IQuery<TrialView>;

/// <summary>Reads the trial and, while it is active, its live stages.</summary>
public sealed class GetTrialQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetTrialQuery, TrialView>
{
    public async ValueTask<TrialView> Handle(GetTrialQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var t = await db.GetTrialAsync(q.TrialId, q.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_TRIAL_NOT_FOUND", "Trial not found");
        IReadOnlyList<string>? lines = t.State is "queued" or "running" && t.JobId is { } j && q.LiveLines is { } live ? await live(j) : null;
        return await RecipeTrials.ViewAsync(db, t, lines, ct);
    }
}

/// <summary>Stops a waiting or running trial; returns its job so the host stops the container.</summary>
public sealed record CancelTrialCommand(Guid WorkspaceId, Guid TrialId) : ICommand<(TrialView Trial, Guid? JobId)>;

/// <summary>Marks the trial and its hidden run cancelled (a later job exit does not overwrite it).</summary>
public sealed class CancelTrialCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CancelTrialCommand, (TrialView Trial, Guid? JobId)>
{
    public async ValueTask<(TrialView Trial, Guid? JobId)> Handle(CancelTrialCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var t = await db.GetTrialAsync(c.TrialId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_TRIAL_NOT_FOUND", "Trial not found");
        if (t.State is not ("queued" or "running")) throw new PreconditionException("TRACK_TRIAL_FINISHED", "This trial has already finished");
        await db.SetTrialStateAsync(t.Id, c.WorkspaceId, "cancelled", "Stopped on request", clock.UtcNow, ct);
        if (await db.GetRunAsync(t.RunId, ct) is { } run && !RunRules.Finished(run.State)) await db.SetRunStateAsync(run.Id, "cancelled", clock.UtcNow, ct);
        return (await RecipeTrials.ViewAsync(db, (await db.GetTrialAsync(t.Id, c.WorkspaceId, ct))!, null, ct), t.JobId);
    }
}

/// <summary>
/// The hidden run's job finished (called next to <see cref="SyncRunStateCommand"/>): records the stages and measurements from the attempt's
/// <c>[mlp-trial]</c> lines, the machine it ran on, and passed / failed. <paramref name="Headline"/> = the error line of the log, if any.
/// </summary>
public sealed record FinishTrialCommand(Guid JobId, Guid? AttemptId, string JobState, IReadOnlyList<string> Lines, string? Headline) : ICommand<bool>;

/// <summary>Passed = the job succeeded and no stage failed. A retry (job back to queued) leaves the trial waiting.</summary>
public sealed class FinishTrialCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<FinishTrialCommand, bool>
{
    public async ValueTask<bool> Handle(FinishTrialCommand c, CancellationToken ct)
    {
        if (c.JobState is "queued" or "running" or "starting") return false;
        if (await db.GetRunByJobAsync(c.JobId, ct) is not { } run || await db.GetTrialByRunAsync(run.Id, run.WorkspaceId, ct) is not { } t) return false;
        var (stages, m) = RecipeTrials.Parse(c.Lines);
        var failed = stages.FirstOrDefault(s => s.State == "failed");
        string state, error = "";
        if (c.JobState == "cancelled") state = "cancelled";
        else if (c.JobState == "succeeded" && failed is null && stages.Count > 0) state = "passed";
        else
        {
            state = "failed";
            error = failed is not null ? $"{failed.Key}: {failed.Detail}"
                : !string.IsNullOrEmpty(c.Headline) ? c.Headline
                : stages.Count == 0 && c.JobState == "succeeded" ? "The trial reported no stages (the recipe image may have an old mlp-shim)"
                : $"The trial job {c.JobState}";
        }
        // Stages that never ended (the process died mid-stage) are failed, so the canvas does not show them running forever.
        if (state != "passed")
            stages = stages.Select(s => s.State is "running" ? s with { State = state == "cancelled" ? "skipped" : "failed" } : s.State is "pending" ? s with { State = "skipped" } : s).ToList();
        Guid? host = c.AttemptId is { } a ? (await db.GetAttemptAsync(a, ct))?.HostIds.FirstOrDefault() : null;
        if (host == Guid.Empty) host = null;
        await db.FinishTrialAsync(t.Id, t.WorkspaceId, state, RecipeTrials.StagesJson(stages), m?.StepsPerS, m?.PeakVramMb, m?.VramEstimated ?? false, m?.ValSPerBatch,
            m?.TrainImages ?? t.TrainImages, m?.ValImages ?? t.ValImages, error, host, clock.UtcNow, ct);
        return true;
    }
}
