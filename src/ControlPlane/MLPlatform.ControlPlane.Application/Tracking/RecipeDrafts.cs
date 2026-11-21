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

/// <summary>Rules shared by the draft handlers (round 1, D-49): defaults, normalising, hashing, diffing.</summary>
public static class RecipeDrafts
{
    public static string Sha(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    public static JsonObject Schema(JsonObject manifest) => manifest["params"] as JsonObject ?? [];

    /// <summary>Parameters a person may set: those of the manifest that are not filled in by the platform
    /// (`from:`).</summary>
    public static bool Settable(JsonObject schema, string key) => schema[key] is JsonObject def && def["from"] is null;

    public static string FirstVariant(JsonObject manifest) =>
        (manifest["variants"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()?["name"]?.ToString() ?? "";

    public static bool HasVariant(JsonObject manifest, string name) =>
        (manifest["variants"] as JsonArray)?.OfType<JsonObject>().Any(v => v["name"]?.ToString() == name) == true;

    /// <summary>Only keys the manifest lets a person set, in key order — the same content always serialises the same
    /// way.</summary>
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

    /// <summary>epochs × (⌊train images / batch⌋ / steps per second + ⌈val images / 64⌉ × seconds per val batch); −20 %
    /// … +50 %.</summary>
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
        var dataSeconds = 0.0;
        if (RecipeTrials.StagesFrom(t.Stages).FirstOrDefault(x => x.Key == "data" && x.State == "passed") is { Seconds: > 0 } ds && t.TrainImages + t.ValImages > 0)
            dataSeconds = ds.Seconds * data.Counts.Values.Sum() / (t.TrainImages + t.ValImages);
        seconds += dataSeconds;
        var host = t.HostId is { } h ? (await db.GetHostAsync(h, ws, ct))?.Name : null;
        return new TrainEstimateView(seconds * 0.8, seconds * 1.5, host ?? "", t.ValSPerBatch is > 0, Math.Round(dataSeconds));
    }

    /// <summary>Freezes the draft into a revision (shared by train and trial): the same content never makes a second
    /// revision.</summary>
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
        catch (NotFoundException) { return new ProjectRecipeView(pr, pr.OriginName, false); }
    }
}
