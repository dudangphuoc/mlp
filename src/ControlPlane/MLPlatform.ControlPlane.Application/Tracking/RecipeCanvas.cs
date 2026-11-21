using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A project recipe with what the list needs from the Catalog: its display name and whether the origin changed since it was taken.</summary>
public sealed record ProjectRecipeView(ProjectRecipeRow Row, string DisplayName, bool OriginChanged);

/// <summary>One difference between a draft and the revision it started from.</summary>
public sealed record DraftDiff(string Field, string Pane, string From, string To);

/// <summary>Everything the canvas shows for a draft: the recipe, the draft, its changes, why it can or cannot train, and the chosen data.</summary>
public sealed record DraftView(ProjectRecipeView Recipe, DraftRow Draft, int BaseRevision, IReadOnlyList<DraftDiff> Changes,
    IReadOnlyList<string> Reasons, VersionSplitInfo? Dataset, bool CanTrial = false, TrialView? Trial = null, TrainEstimateView? Estimate = null,
    TrialRow? PassedTrial = null);

/// <summary>Estimated duration of the real training from a passed trial's measurements, as a range.</summary>
public sealed record TrainEstimateView(double LowSeconds, double HighSeconds, string Source, bool ValMeasured = false, double DataSeconds = 0);

/// <summary>Values a person can set on a draft.</summary>
public sealed record DraftInput(Guid ProjectRecipeId, string Variant, string ParamsJson, Guid? DatasetVersionId, string StartKind, string TrainKind,
    Guid? RetrainOfRunId, Guid? HostId, int Gpus, double Cpus, string RunName);

/// <summary>Rules shared by the draft handlers (round 1, D-49): defaults, normalising, hashing, diffing.</summary>
public static class RecipeDrafts
{
    public static string Sha(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    public static JsonObject Schema(JsonObject manifest) => manifest["params"] as JsonObject ?? [];

    /// <summary>Parameters a person may set: those of the manifest that are not filled in by the platform (`from:`).</summary>
    public static bool Settable(JsonObject schema, string key) => schema[key] is JsonObject def && def["from"] is null;

    public static string FirstVariant(JsonObject manifest) =>
        (manifest["variants"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()?["name"]?.ToString() ?? "";

    public static bool HasVariant(JsonObject manifest, string name) =>
        (manifest["variants"] as JsonArray)?.OfType<JsonObject>().Any(v => v["name"]?.ToString() == name) == true;

    /// <summary>Only keys the manifest lets a person set, in key order — the same content always serialises the same way.</summary>
    public static JsonObject Normalise(JsonObject values, JsonObject schema)
    {
        var res = new JsonObject();
        foreach (var k in values.Select(p => p.Key).Where(k => Settable(schema, k)).Order(StringComparer.Ordinal))
            if (values[k] is { } v) res[k] = v.DeepClone();
        return res;
    }

    public static JsonObject Parse(string? json)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? []; }
        catch (JsonException) { throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Invalid JSON" }); }
    }

    /// <summary>Pane of the canvas a parameter belongs to: the manifest's `pane`, else a fixed default.</summary>
    public static string Pane(JsonObject schema, string key) =>
        schema[key]?["pane"]?.ToString() is { Length: > 0 } p ? p : key switch { "batch" => "batch", "epochs" => "schedule", _ => "objective" };

    /// <summary>Content hash of a revision, and the same without the parameters that need no new trial.</summary>
    public static (string Content, string TrialKey) Hashes(string manifestSha, string variant, JsonObject parameters, JsonObject schema)
    {
        var forTrial = new JsonObject();
        foreach (var (k, v) in parameters)
            if (v is not null && schema[k]?["affectsTrial"]?.GetValue<bool>() != false && k is not ("epochs" or "seed")) forTrial[k] = v.DeepClone();
        return (Sha($"{manifestSha}\n{variant}\n{parameters.ToJsonString()}"), Sha($"{manifestSha}\n{variant}\n{forTrial.ToJsonString()}"));
    }

    public static List<DraftDiff> Diff(string baseVariant, JsonObject baseParams, string variant, JsonObject parameters, JsonObject schema)
    {
        var res = new List<DraftDiff>();
        if (baseVariant != variant) res.Add(new DraftDiff("variant", "model", baseVariant, variant));
        foreach (var k in baseParams.Select(p => p.Key).Union(parameters.Select(p => p.Key)).Order(StringComparer.Ordinal))
        {
            // "" = the recipe's default; strings without quotes, so the canvas can show and restore the value as typed.
            string Show(JsonObject o) => o[k] is not { } v ? "" : v is JsonValue jv && jv.TryGetValue<string>(out var text) ? text : v.ToJsonString();
            if (Show(baseParams) != Show(parameters)) res.Add(new DraftDiff("params." + k, Pane(schema, k), Show(baseParams), Show(parameters)));
        }
        return res;
    }

    /// <summary>Builds the canvas state: one Catalog lookup, the base revision, and the dataset's split.</summary>
    public static async Task<DraftView> StateAsync(IReadSession db, IMediator mediator, Guid ws, ProjectRecipeRow pr, DraftRow d, string task, CancellationToken ct)
    {
        var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
        var schema = Schema(manifest);
        var view = await ViewAsync(mediator, ws, pr, ct);
        var parameters = Parse(d.Params);
        RevisionRow? basis = d.BaseRevisionId is { } b ? await db.GetRevisionAsync(b, ws, ct) : null;
        var changes = basis is null
            ? Diff(FirstVariant(manifest), [], d.Variant, parameters, schema)
            : Diff(basis.Variant, Parse(basis.Params), d.Variant, parameters, schema);
        VersionSplitInfo? data = d.DatasetVersionId is { } v ? await mediator.Send(new DescribeVersionSplitCommand(ws, v), ct) : null;
        var reasons = new List<string>();
        if (!HasVariant(manifest, d.Variant)) reasons.Add("Pick a variant");
        if (data is null && TrainParams.NeedsData(manifest, task)) reasons.Add("Pick a dataset version");
        if (data is { State: "none" }) reasons.Add("This version has no train/val split — split it first");
        if (data is not null && data.Task != task) reasons.Add($"Dataset has task {data.Task}, but the project uses {task}");
        reasons.AddRange(TrainParams.Validate(parameters, schema).Values);
        if (d.TrainKind == "retrain" && d.RetrainOfRunId is { } of && await db.GetRunAsync(of, ct) is { } repeated && repeated.DatasetVersionId == d.DatasetVersionId)
            reasons.Add("Train again needs a different dataset version than the run it repeats");
        // R21: a training needs a passed trial of the same content on the same data and start (and machine, when the draft pins one).
        var canTrial = reasons.Count == 0;
        var trialKey = Hashes(pr.OriginManifestSha256, d.Variant, Normalise(parameters, schema), schema).TrialKey;
        var latest = await db.LatestTrialAsync(pr.Id, ws, trialKey, d.DatasetVersionId, d.StartKind, d.HostId, false, ct);
        var passed = latest is { State: "passed" } ? latest : await db.LatestTrialAsync(pr.Id, ws, trialKey, d.DatasetVersionId, d.StartKind, d.HostId, true, ct);
        TrialView? trial = latest is null ? null : await RecipeTrials.ViewAsync(db, latest, null, ct);
        TrainEstimateView? estimate = null;
        if (passed is null) reasons.Add("Run a trial with these settings first");
        else estimate = await EstimateAsync(db, ws, passed, parameters, schema, data, ct);
        return new DraftView(view, d, basis?.Number ?? 0, changes, reasons, data, canTrial, trial, estimate, passed);
    }

    /// <summary>A parameter's value: what the draft set, else the manifest default.</summary>
    public static JsonNode? Value(JsonObject parameters, JsonObject schema, string key) => parameters[key] ?? schema[key]?["default"];

    public static int IntValue(JsonObject parameters, JsonObject schema, string key, int fallback) =>
        Value(parameters, schema, key) is JsonValue v && v.TryGetValue<double>(out var n) && n >= 1 ? (int)n : fallback;

    /// <summary>
    /// epochs × (⌊train images / batch⌋ / steps per second + ⌈val images / 64⌉ × seconds per val batch); −20 % … +50 %.
    /// Without a measured validation time only the training steps are counted, and the source says so.
    /// </summary>
    private static async Task<TrainEstimateView?> EstimateAsync(IReadSession db, Guid ws, TrialRow t, JsonObject parameters, JsonObject schema, VersionSplitInfo? data,
        CancellationToken ct)
    {
        if (t.StepsPerS is not > 0 || data is null) return null;
        var epochs = IntValue(parameters, schema, "epochs", 1);
        var batch = IntValue(parameters, schema, "batch", 1);
        var train = data.Counts.GetValueOrDefault("train");
        var val = data.Counts.GetValueOrDefault("val");
        var perEpoch = Math.Floor((double)train / batch) / t.StepsPerS.Value;
        if (t.ValSPerBatch is > 0) perEpoch += Math.Ceiling(val / 64.0) * t.ValSPerBatch.Value;
        var seconds = epochs * perEpoch;
        // Data preparation before the first step (run #9: 8.5 of 15.5 minutes): the trial's data stage scaled by how many images the full
        // version has against the trial's subset. A new run gets its own volume, so nothing is cached from the trial.
        var dataSeconds = 0.0;
        if (RecipeTrials.StagesFrom(t.Stages).FirstOrDefault(x => x.Key == "data" && x.State == "passed") is { Seconds: > 0 } ds && t.TrainImages + t.ValImages > 0)
            dataSeconds = ds.Seconds * data.Counts.Values.Sum() / (t.TrainImages + t.ValImages);
        seconds += dataSeconds;
        var host = t.HostId is { } h ? (await db.GetHostAsync(h, ws, ct))?.Name : null;
        return new TrainEstimateView(seconds * 0.8, seconds * 1.5, host ?? "", t.ValSPerBatch is > 0, Math.Round(dataSeconds));
    }

    /// <summary>
    /// Freezes the draft into a revision (shared by train and trial): the same content never makes a second revision.
    /// Returns the revision and its trial key.
    /// </summary>
    public static async Task<(RevisionRow Revision, string TrialKey)> FreezeAsync(IWriteSession db, IClock clock, Guid ws, Guid uid, ProjectRecipeRow pr, DraftRow d,
        CancellationToken ct)
    {
        var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
        var schema = Schema(manifest);
        var parameters = Normalise(Parse(d.Params), schema);
        var (content, trialKey) = Hashes(pr.OriginManifestSha256, d.Variant, parameters, schema);
        var rev = await db.GetRevisionByContentAsync(pr.Id, ws, content, ct);
        if (rev is null)
        {
            var id = Ids.New();
            var number = (int)await db.NextRevisionNumberAsync(pr.Id, ws, ct);
            await db.InsertRevisionAsync(id, ws, pr.Id, number, d.BaseRevisionId, pr.OriginManifest, pr.OriginManifestSha256, d.Variant,
                parameters.ToJsonString(), content, trialKey, uid, clock.UtcNow, ct);
            rev = (await db.GetRevisionAsync(id, ws, ct))!;
        }
        return (rev, trialKey);
    }

    public static async Task<ProjectRecipeView> ViewAsync(IMediator mediator, Guid ws, ProjectRecipeRow pr, CancellationToken ct)
    {
        try
        {
            var recipe = await mediator.Query(new GetRecipeQuery(ws, pr.OriginName), ct);
            return new ProjectRecipeView(pr, recipe.DisplayName, Sha(recipe.Manifest) != pr.OriginManifestSha256);
        }
        catch (NotFoundException) { return new ProjectRecipeView(pr, pr.OriginName, false); }   // removed from the Catalog: the project keeps its copy
    }
}

/// <summary>Takes a Catalog recipe into a project (idempotent): the project gets its own copy of the manifest.</summary>
public sealed record AdoptRecipeCommand(Guid WorkspaceId, Guid ProjectId, string Recipe) : ICommand<ProjectRecipeView>;

/// <summary>Copies the manifest as it is now; a second call for the same recipe returns the existing copy.</summary>
public sealed class AdoptRecipeCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<AdoptRecipeCommand, ProjectRecipeView>
{
    public async ValueTask<ProjectRecipeView> Handle(AdoptRecipeCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var project = await db.GetProjectAsync(c.ProjectId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_PROJECT_NOT_FOUND", "Project not found");
        if (await db.GetProjectRecipeByNameAsync(project.Id, c.WorkspaceId, c.Recipe, ct) is { } existing)
            return await RecipeDrafts.ViewAsync(mediator, c.WorkspaceId, existing, ct);
        var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, c.Recipe), ct);
        if (!recipe.Tasks.Contains(project.Task))
            throw new PreconditionException("CATALOG_TASK_UNSUPPORTED", $"Recipe {recipe.Name} does not support task {project.Task}");
        if (JsonNode.Parse(recipe.Manifest)?["entrypoints"]?["train"] is not JsonObject)
            throw new PreconditionException("CATALOG_NO_TRAIN", $"Recipe {recipe.Name} has no train entrypoint");
        var id = Ids.New();
        await db.InsertProjectRecipeAsync(id, c.WorkspaceId, project.Id, recipe.Id, recipe.Name, recipe.Manifest, RecipeDrafts.Sha(recipe.Manifest), uid, clock.UtcNow, ct);
        return new ProjectRecipeView((await db.GetProjectRecipeAsync(id, c.WorkspaceId, ct))!, recipe.DisplayName, false);
    }
}

/// <summary>The recipes a project works with.</summary>
public sealed record ListProjectRecipesQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<ProjectRecipeView>>;

/// <summary>Lists the project's copies and compares each with the Catalog recipe it came from.</summary>
public sealed class ListProjectRecipesQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IMediator mediator) : IQueryHandler<ListProjectRecipesQuery, IReadOnlyList<ProjectRecipeView>>
{
    public async ValueTask<IReadOnlyList<ProjectRecipeView>> Handle(ListProjectRecipesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var res = new List<ProjectRecipeView>();
        foreach (var pr in await db.ListProjectRecipesAsync(q.ProjectId, q.WorkspaceId, ct)) res.Add(await RecipeDrafts.ViewAsync(mediator, q.WorkspaceId, pr, ct));
        return res;
    }
}

/// <summary>The caller's draft of a project recipe; made from the recipe's defaults the first time.</summary>
public sealed record GetDraftCommand(Guid WorkspaceId, Guid ProjectRecipeId) : ICommand<DraftView>;

/// <summary>Creates the draft on first use so the canvas always has something to show.</summary>
public sealed class GetDraftCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<GetDraftCommand, DraftView>
{
    public async ValueTask<DraftView> Handle(GetDraftCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var pr = await db.GetProjectRecipeAsync(c.ProjectRecipeId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_RECIPE_NOT_FOUND", "Project recipe not found");
        var project = (await db.GetProjectAsync(pr.ProjectId, c.WorkspaceId, ct))!;
        var draft = await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct);
        if (draft is null)
        {
            var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
            var gpu = manifest["environment"]?["gpu"]?.ToString() == "required" ? 1 : 0;
            await db.InsertDraftAsync(Ids.New(), c.WorkspaceId, pr.Id, uid, null, RecipeDrafts.FirstVariant(manifest), "{}", null, "pretrained", "fine_tune", null, null,
                gpu, 4, "", clock.UtcNow, ct);
            draft = (await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct))!;
        }
        return await RecipeDrafts.StateAsync(db, mediator, c.WorkspaceId, pr, draft, project.Task, ct);
    }
}

/// <summary>Saves the caller's draft. <paramref name="Etag"/> is the one the client last read; a stale one is refused.</summary>
public sealed record UpdateDraftCommand(Guid WorkspaceId, DraftInput Draft, int Etag) : ICommand<DraftView>;

/// <summary>Checks the shape of the values (unknown parameters, kinds, limits); whether they can train is the gate's job, not a save error.</summary>
public sealed class UpdateDraftCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<UpdateDraftCommand, DraftView>
{
    public async ValueTask<DraftView> Handle(UpdateDraftCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var i = c.Draft;
        var pr = await db.GetProjectRecipeAsync(i.ProjectRecipeId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_RECIPE_NOT_FOUND", "Project recipe not found");
        var project = (await db.GetProjectAsync(pr.ProjectId, c.WorkspaceId, ct))!;
        var draft = await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_DRAFT_NOT_FOUND", "Open the recipe first");
        var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
        var schema = RecipeDrafts.Schema(manifest);
        var values = RecipeDrafts.Parse(i.ParamsJson);
        var unknown = values.Select(p => p.Key).Where(k => !RecipeDrafts.Settable(schema, k)).ToList();
        if (unknown.Count > 0)
            throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Parameters not in the recipe: " + string.Join(", ", unknown) });
        if (i.StartKind is not ("pretrained" or "backbone")) throw new InvalidArgumentException("TRACK_DRAFT_INVALID", "start_kind is pretrained or backbone");
        if (i.StartKind == "backbone" && manifest["entrypoints"]?["train"]?["fromScratch"]?.GetValue<bool>() != true)
            throw new PreconditionException("CATALOG_FROM_SCRATCH_UNSUPPORTED", "Recipe does not support training from scratch (20/08 §4.2)");
        if (i.TrainKind is not ("fine_tune" or "retrain")) throw new InvalidArgumentException("TRACK_DRAFT_INVALID", "train_kind is fine_tune or retrain");
        // R5: train again is entered only through "Train again from #n" (ResetDraft); while in it, the recipe content stays the revision of #n.
        Guid? retrainOf = null;
        if (i.TrainKind == "retrain")
        {
            if (draft.TrainKind != "retrain" || draft.RetrainOfRunId is null || draft.BaseRevisionId is null)
                throw new PreconditionException("TRACK_RETRAIN_NOT_STARTED", "Open \"Train again\" from a finished run first");
            var locked = (await db.GetRevisionAsync(draft.BaseRevisionId.Value, c.WorkspaceId, ct))!;
            if (i.Variant != locked.Variant || RecipeDrafts.Normalise(values, schema).ToJsonString() != RecipeDrafts.Normalise(RecipeDrafts.Parse(locked.Params), schema).ToJsonString()
                || i.StartKind != draft.StartKind)
                throw new PreconditionException("TRACK_RETRAIN_LOCKED", "Train again keeps the recipe of the run — switch to Fine-tune to change it");
            retrainOf = draft.RetrainOfRunId;
        }
        if (i.Gpus is < 0 or > 16 || i.Cpus is <= 0 or > 512) throw new InvalidArgumentException("TRACK_RESOURCES_INVALID", "GPUs must be between 0 and 16, CPUs above 0");
        if (i.RunName.Trim().Length > 80) throw new InvalidArgumentException("TRACK_NAME_INVALID", "Run name must be at most 80 characters");
        if (i.DatasetVersionId is { } dv && await db.GetVersionAsync(dv, c.WorkspaceId, ct) is null)
            throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var saved = await db.UpdateDraftAsync(draft.Id, c.WorkspaceId, draft.BaseRevisionId, i.Variant, RecipeDrafts.Normalise(values, schema).ToJsonString(), i.DatasetVersionId,
            i.StartKind, i.TrainKind, retrainOf, i.HostId, i.Gpus, i.Cpus, i.RunName.Trim(), c.Etag, clock.UtcNow, ct);
        if (saved == 0) throw new AbortedException("TRACK_DRAFT_CONFLICT", "The draft was changed in another tab — reload");
        return await RecipeDrafts.StateAsync(db, mediator, c.WorkspaceId, pr, (await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct))!, project.Task, ct);
    }
}

/// <summary>
/// Replaces the caller's draft: from a revision, from a run ("cook again from #n" — also its data, starting point and machine),
/// or from the Catalog recipe as it is now (which also moves the project's copy to that manifest).
/// </summary>
public sealed record ResetDraftCommand(Guid WorkspaceId, Guid ProjectRecipeId, int RevisionNumber, Guid? RunId, bool Origin, bool Retrain = false) : ICommand<DraftView>;

/// <summary>A run without a revision (older runs, pipelines, sweeps) is rebuilt from the fields the run kept.</summary>
public sealed class ResetDraftCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<ResetDraftCommand, DraftView>
{
    public async ValueTask<DraftView> Handle(ResetDraftCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var pr = await db.GetProjectRecipeAsync(c.ProjectRecipeId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_RECIPE_NOT_FOUND", "Project recipe not found");
        var project = (await db.GetProjectAsync(pr.ProjectId, c.WorkspaceId, ct))!;
        var d = (await mediator.Send(new GetDraftCommand(c.WorkspaceId, pr.Id), ct)).Draft;
        var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
        var schema = RecipeDrafts.Schema(manifest);
        Guid? basis = null, version = d.DatasetVersionId, host = d.HostId;
        string variant, parameters, start = d.StartKind;
        int gpus = d.Gpus; double cpus = d.Cpus;
        string trainKind = "fine_tune";
        Guid? retrainOf = null;
        if (c.Retrain && c.RunId is null) throw new InvalidArgumentException("TRACK_DRAFT_INVALID", "Train again needs a run");
        if (c.RunId is { } runId)
        {
            var run = await db.GetRunAsync(runId, ct);
            if (run is null || run.WorkspaceId != c.WorkspaceId || run.ProjectId != pr.ProjectId) throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
            if (run.Recipe != pr.OriginName) throw new PreconditionException("TRACK_RECIPE_MISMATCH", $"Run #{run.Number} used recipe {run.Recipe}");
            var extra = (await db.RunExtrasAsync(c.WorkspaceId, [run.Id], ct)).FirstOrDefault();
            var rev = extra?.RevisionId is { } rid ? await db.GetRevisionAsync(rid, c.WorkspaceId, ct) : null;
            basis = rev?.Id;
            variant = rev?.Variant ?? run.Variant;
            // Without a revision the run's stored parameters include values the platform filled in (class count): keep what a person can set.
            parameters = rev?.Params ?? RecipeDrafts.Normalise(RecipeDrafts.Parse(run.Params), schema).ToJsonString();
            (version, start, host, gpus, cpus) = (run.DatasetVersionId, run.FromScratch ? "backbone" : "pretrained", extra?.HostId, run.Gpus, run.Cpus);
            if (c.Retrain)
            {
                // R5: only a finished run that cooked a revision; same recipe and start, the person picks another dataset version.
                if (run.State != "succeeded") throw new PreconditionException("TRACK_RETRAIN_NOT_FINISHED", $"Run #{run.Number} has not finished successfully");
                if (rev is null) throw new PreconditionException("TRACK_RETRAIN_NO_REVISION", $"Run #{run.Number} was trained before revisions existed — cook it again instead");
                if (rev.ManifestSha256 != pr.OriginManifestSha256)
                    throw new PreconditionException("TRACK_RETRAIN_RECIPE_CHANGED", $"The project's recipe changed since run #{run.Number} — cook it again instead");
                (trainKind, retrainOf, version, start) = ("retrain", run.Id, null, extra?.StartKind ?? start);
            }
        }
        else if (c.Origin)
        {
            var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, pr.OriginName), ct);
            await db.UpdateProjectRecipeOriginAsync(pr.Id, c.WorkspaceId, recipe.Id, recipe.Manifest, RecipeDrafts.Sha(recipe.Manifest), ct);
            pr = (await db.GetProjectRecipeAsync(pr.Id, c.WorkspaceId, ct))!;
            manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
            schema = RecipeDrafts.Schema(manifest);
            variant = RecipeDrafts.HasVariant(manifest, d.Variant) ? d.Variant : RecipeDrafts.FirstVariant(manifest);
            parameters = RecipeDrafts.Normalise(RecipeDrafts.Parse(d.Params), schema).ToJsonString();
        }
        else
        {
            RevisionRow? rev = c.RevisionNumber > 0
                ? await db.GetRevisionByNumberAsync(pr.Id, c.WorkspaceId, c.RevisionNumber, ct) ?? throw new NotFoundException("TRACK_REVISION_NOT_FOUND", "Revision not found")
                : null;
            (basis, variant, parameters) = (rev?.Id, rev?.Variant ?? RecipeDrafts.FirstVariant(manifest), rev?.Params ?? "{}");
        }
        await db.UpdateDraftAsync(d.Id, c.WorkspaceId, basis, variant, parameters, version, start, trainKind, retrainOf, host, gpus, cpus, d.RunName, -1, clock.UtcNow, ct);
        return await RecipeDrafts.StateAsync(db, mediator, c.WorkspaceId, pr, (await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct))!, project.Task, ct);
    }
}

/// <summary>Frozen revisions of a project recipe, newest first.</summary>
public sealed record ListRevisionsQuery(Guid WorkspaceId, Guid ProjectRecipeId) : IQuery<(IReadOnlyList<RevisionListRow> Rows, IReadOnlyDictionary<Guid, string> Names)>;

/// <summary>Lists revisions and resolves who froze them in one lookup.</summary>
public sealed class ListRevisionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth)
    : IQueryHandler<ListRevisionsQuery, (IReadOnlyList<RevisionListRow> Rows, IReadOnlyDictionary<Guid, string> Names)>
{
    public async ValueTask<(IReadOnlyList<RevisionListRow> Rows, IReadOnlyDictionary<Guid, string> Names)> Handle(ListRevisionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var rows = await db.ListRevisionsAsync(q.ProjectRecipeId, q.WorkspaceId, ct);
        var ids = rows.Select(r => r.CreatedBy).Distinct().ToArray();
        var names = ids.Length == 0 ? [] : (await db.UserNamesAsync(ids, ct)).ToDictionary(u => u.Id, u => u.DisplayName);
        return (rows, names);
    }
}

/// <summary>Trains the caller's draft (canvas path): freezes it into a revision, starts the run, and records what the run cooked.</summary>
public sealed record StartDraftTrainingCommand(Guid WorkspaceId, Guid ProjectRecipeId) : ICommand<RunRow>;

/// <summary>The same content never makes a second revision: an unchanged draft trains its existing revision again.</summary>
public sealed class StartDraftTrainingCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<StartDraftTrainingCommand, RunRow>
{
    public async ValueTask<RunRow> Handle(StartDraftTrainingCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var state = await mediator.Send(new GetDraftCommand(c.WorkspaceId, c.ProjectRecipeId), ct);
        if (!state.CanTrial) throw new PreconditionException("TRACK_DRAFT_NOT_READY", state.Reasons[0]);
        if (state.PassedTrial is not { } trial) throw new PreconditionException("TRACK_TRIAL_REQUIRED", "Run a trial with these settings first");
        var (pr, d) = (state.Recipe.Row, state.Draft);
        if (d.TrainKind == "retrain" && d.RetrainOfRunId is { } of && (await db.GetRunAsync(of, ct))?.DatasetVersionId == d.DatasetVersionId)
            throw new InvalidArgumentException("TRACK_RETRAIN_SAME_DATA", "Train again needs a different dataset version than the run it repeats");
        var (rev, _) = await RecipeDrafts.FreezeAsync(db, clock, c.WorkspaceId, uid, pr, d, ct);
        // A draft that lets any machine train goes to the machine the trial measured, so the estimate and the memory check hold.
        var host = d.HostId ?? trial.HostId;
        var run = await mediator.Send(new StartTrainingCommand(c.WorkspaceId, pr.ProjectId, d.RunName, pr.OriginName, rev.Variant, "", rev.Params,
            d.StartKind == "backbone", d.Cpus, d.Gpus, null, null, d.DatasetVersionId, rev.Manifest, host,
            // D-50: the GPU budget of a real training = what the trial measured plus a quarter and 512 MB of headroom.
            GpuMemoryMb: trial.PeakVramMb is > 0 ? (int)(trial.PeakVramMb.Value * 1.25) + 512 : 0), ct);
        await db.SetRunRevisionAsync(run.Id, c.WorkspaceId, rev.Id, d.TrainKind, d.StartKind, d.TrainKind == "retrain" ? d.RetrainOfRunId : null, host, ct);
        await db.SetRunTrialAsync(run.Id, c.WorkspaceId, trial.Id, ct);
        // The draft now sits on the revision it just cooked: the next edit shows as a change against it.
        // A train-again draft goes back to ordinary fine-tuning once its run started.
        await db.UpdateDraftAsync(d.Id, c.WorkspaceId, rev.Id, d.Variant, d.Params, d.DatasetVersionId, d.StartKind, "fine_tune", null, d.HostId, d.Gpus, d.Cpus, "",
            -1, clock.UtcNow, ct);
        return run;
    }
}
