using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Train from the UI (M09 §5.1): recipe + variant + task + params → run + job running `mlp-shim` in the
/// recipe's image.</summary>
public sealed record StartTrainingCommand(Guid WorkspaceId, Guid ProjectId, string Name, string Recipe, string Variant, string Task,
    string ParamsJson, bool FromScratch, double Cpus, int Gpus, string? ImageOverride, string? Network, Guid? DatasetVersionId = null,
    string? ManifestOverride = null, Guid? HostId = null, TrialSpec? Trial = null, int GpuMemoryMb = 0) : ICommand<RunRow>;

public sealed partial class StartTrainingCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator, MLPlatform.Storage.S3Client s3)
    : ICommandHandler<StartTrainingCommand, RunRow>
{
    private const int MaxInlineDataFilesBytes = 64 * 1024;
    private const int MaxGpus = 16;
    private const int MaxRunNameLength = 80;

    private sealed record TrainingData(JsonArray? Files, string? FilesUrl, JsonObject Info, string? Cache, Guid? DatasetId);

    public async ValueTask<RunRow> Handle(StartTrainingCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var project = await db.GetProjectAsync(c.ProjectId, c.WorkspaceId, ct)
                      ?? throw new NotFoundException("TRACK_PROJECT_NOT_FOUND", "Project not found");
        var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, c.Recipe), ct);
        var manifest = JsonNode.Parse(c.ManifestOverride ?? recipe.Manifest)!.AsObject();
        var task = string.IsNullOrEmpty(c.Task) ? project.Task : c.Task;
        var (variant, image) = CheckRecipe(c, recipe, manifest, task);
        var parameters = CheckParameters(c, recipe, manifest, task);
        var runId = Ids.New();
        var data = await ResolveDataAsync(c, manifest, task, runId, parameters, ct);
        var shimSpec = ShimSpec(c, manifest, task, variant, parameters, data);
        var (number, name) = await NameRunAsync(c, project.Id, ct);
        var jobId = await SubmitAsync(c, manifest, image, name, shimSpec, ct);
        await db.InsertRunAsync(runId, project.Id, c.WorkspaceId, name, jobId, uid,
            recipe.Name, c.Variant, task, parameters.ToJsonString(), PrimaryMetric(manifest, task), clock.UtcNow, number, c.DatasetVersionId, c.FromScratch, c.Gpus,
            c.Cpus <= 0 ? 1 : c.Cpus, ct);
        if (c.Trial is not null) await db.MarkRunTrialAsync(runId, c.WorkspaceId, ct);
        return (await db.GetRunAsync(runId, ct))!;
    }

    private static (JsonObject Variant, string Image) CheckRecipe(StartTrainingCommand c, RecipeRow recipe, JsonObject manifest, string task)
    {
        if (!recipe.Tasks.Contains(task))
            throw new PreconditionException("CATALOG_TASK_UNSUPPORTED", $"Recipe {recipe.Name} does not support task {task}");
        var train = manifest["entrypoints"]?["train"] as JsonObject
                    ?? throw new PreconditionException("CATALOG_NO_TRAIN", $"Recipe {recipe.Name} has no train entrypoint");
        if (c.FromScratch && train["fromScratch"]?.GetValue<bool>() != true)
            throw new PreconditionException("CATALOG_FROM_SCRATCH_UNSUPPORTED", "Recipe does not support training from scratch (20/08 §4.2)");
        var variant = (manifest["variants"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(v => v["name"]?.ToString() == c.Variant)
                      ?? throw new NotFoundException("CATALOG_VARIANT_NOT_FOUND", $"Variant {c.Variant} not found");
        if (!string.IsNullOrEmpty(c.ImageOverride)) return (variant, c.ImageOverride);
        if (recipe.State != "invalid") return (variant, $"recipes/{recipe.Name}:current");
        throw new PreconditionException("CATALOG_RECIPE_NOT_VERIFIED", $"Recipe {recipe.Name} is not valid ({recipe.State})");
    }

    private static JsonObject CheckParameters(StartTrainingCommand c, RecipeRow recipe, JsonObject manifest, string task)
    {
        JsonObject parameters;
        try { parameters = JsonNode.Parse(string.IsNullOrWhiteSpace(c.ParamsJson) ? "{}" : c.ParamsJson) as JsonObject ?? []; }
        catch (JsonException) { throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Invalid JSON" }); }
        var unknown = parameters.Select(p => p.Key).Where(k => manifest["params"]?[k] is null).ToList();
        if (unknown.Count > 0)
            throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Parameters not in the recipe: " + string.Join(", ", unknown) });
        var invalid = TrainParams.Validate(parameters, manifest["params"] as JsonObject ?? []);
        if (invalid.Count > 0)
            throw new InvalidArgumentException("TRACK_PARAMS_INVALID", invalid.First().Value, invalid);
        if (c.DatasetVersionId is null && string.IsNullOrEmpty(c.ImageOverride) && TrainParams.NeedsData(manifest, task))
            throw new PreconditionException("TRACK_DATASET_REQUIRED", "Pick a dataset version — this recipe trains on data");
        if (c.Gpus < 0 || c.Gpus > MaxGpus)
            throw new InvalidArgumentException("TRACK_RESOURCES_INVALID", "GPUs must be between 0 and 16");
        if (c.Gpus == 0 && string.IsNullOrEmpty(c.ImageOverride) && manifest["environment"]?["gpu"]?.ToString() == "required")
            throw new PreconditionException("TRACK_GPU_REQUIRED", $"Recipe {recipe.Name} needs a GPU");
        return parameters;
    }

    private static string PrimaryMetric(JsonObject manifest, string task)
    {
        var primary = manifest["metrics"]?["primary"]?.ToString() ?? "";
        return !primary.Contains('{') ? primary : MLPlatform.Recipes.Template.Render(primary, (string n, out JsonNode? v) =>
        { v = n == "task" ? JsonValue.Create(task) : null; return n == "task"; }, MLPlatform.Recipes.RenderMode.Raw);
    }

    private async Task<TrainingData> ResolveDataAsync(StartTrainingCommand c, JsonObject manifest, string task, Guid runId, JsonObject parameters, CancellationToken ct)
    {
        if (c.DatasetVersionId is not { } versionId) return new TrainingData(null, null, [], null, null);
        var (version, datasetTask, files) = await mediator.Query(new MLPlatform.ControlPlane.Application.Data.ResolveVersionFilesQuery(c.WorkspaceId, versionId), ct);
        if (c.Trial is { } subset) files = files.Where(f => subset.Files.Contains(f.Path)).ToList();
        if (datasetTask != task) throw new PreconditionException("CATALOG_TASK_UNSUPPORTED", $"Dataset has task {datasetTask}, but the project/recipe uses {task}");
        JsonArray? inline = new JsonArray(files.Select(f => (JsonNode)f.ToJson()).ToArray());
        string? filesUrl = null;
        if (inline.ToJsonString().Length > MaxInlineDataFilesBytes)
        {
            var key = $"{c.WorkspaceId}/runs/{runId}/data-files.json";
            await s3.PutAsync(Buckets.Artifacts, key, System.Text.Encoding.UTF8.GetBytes(inline.ToJsonString()), "application/json", ct);
            filesUrl = s3.Presign(HttpMethod.Get, Buckets.Artifacts, key, TimeSpan.FromDays(7), MLPlatform.Storage.Audience.Workload);
            inline = null;
        }
        var info = await DatasetInfoAsync(version.DatasetId, manifest, parameters, ct);
        return new TrainingData(inline, filesUrl, info, MLPlatform.ControlPlane.Application.Data.DataCacheRules.Mount(version.DatasetId), version.DatasetId);
    }

    private async Task<JsonObject> DatasetInfoAsync(Guid datasetId, JsonObject manifest, JsonObject parameters, CancellationToken ct)
    {
        var info = new JsonObject();
        if (await db.GetCurrentSchemaAsync(datasetId, ct) is not { } schema) return info;
        var names = ClassNames(schema.Classes);
        info["classes.count"] = names.Count;
        info["classes.names_csv"] = string.Join(",", names);
        info["classes.names_json"] = new JsonArray(names.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()).ToJsonString();
        foreach (var (k, def) in manifest["params"] as JsonObject ?? [])
            if (def?["from"]?.ToString() == "dataset.classes.count" && !parameters.ContainsKey(k)) parameters[k] = names.Count;
        return info;
    }

    private static JsonObject ShimSpec(StartTrainingCommand c, JsonObject manifest, string task, JsonObject variant, JsonObject parameters, TrainingData data)
    {
        var weightsUrl = !c.FromScratch && variant["weights"]?.ToString() is { } wu && wu.StartsWith("https://", StringComparison.Ordinal) ? wu : null;
        var spec = new JsonObject
        {
            ["manifest"] = manifest.DeepClone(), ["entrypoint"] = "train", ["task"] = task, ["variant"] = variant.DeepClone(),
            ["params"] = parameters.DeepClone(), ["output"] = "/mnt/output", ["data"] = "/mnt/data",
            ["weights"] = c.FromScratch ? null : "/mnt/weights/" + (variant["name"]?.ToString() ?? "w"),
            ["weightsRef"] = c.FromScratch ? null : variant["weights"]?.ToString(),
            ["recipeSrc"] = "/opt/recipe/src",
            ["weightsUrl"] = weightsUrl,
            ["dataFiles"] = data.Files,
            ["dataFilesUrl"] = data.FilesUrl,
            ["dataset"] = data.Info,
            ["hardware"] = new JsonObject { ["gpus_per_node"] = Math.Max(1, c.Gpus), ["device"] = c.Gpus > 0 ? "cuda" : "cpu" },
            ["datasetVersionId"] = c.DatasetVersionId?.ToString(),
            ["datasetId"] = data.DatasetId?.ToString(),
            ["dataCache"] = data.Cache,
        };
        if (c.Trial is { } trial) spec["trial"] = trial.Passes.DeepClone();
        return spec;
    }

    private async Task<(int Number, string Name)> NameRunAsync(StartTrainingCommand c, Guid projectId, CancellationToken ct)
    {
        var number = (int)(c.Trial is null ? await db.NextRunNumberAsync(projectId, c.WorkspaceId, ct) : await db.NextTrialRunNumberAsync(projectId, c.WorkspaceId, ct));
        var name = string.IsNullOrWhiteSpace(c.Name) ? c.Trial is null ? $"{c.Variant}-{number}" : $"trial-{c.Variant}" : c.Name.Trim();
        if (name.Length > MaxRunNameLength) throw new InvalidArgumentException("TRACK_NAME_INVALID", "Run name must be at most 80 characters");
        return (number, name);
    }

    private async Task<Guid> SubmitAsync(StartTrainingCommand c, JsonObject manifest, string image, string name, JsonObject shimSpec, CancellationToken ct)
    {
        var env = new Dictionary<string, string> { ["MLP_JOB_SPEC"] = shimSpec.ToJsonString() };
        if (manifest["environment"]?["shmSizeGb"] is JsonValue shm && shm.TryGetValue<int>(out var shmGb) && shmGb > 0)
            env["MLP_SHM_GB"] = shmGb.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "train", name,
            new JobSpecDto
            {
                Image = image, Command = ["mlp-shim", "run", "--spec-env", "MLP_JOB_SPEC"], Cpus = c.Cpus <= 0 ? 1 : c.Cpus, Gpus = c.Gpus,
                GpuMemoryMb = c.GpuMemoryMb, Env = env, Network = c.Network, Hosts = c.HostId is { } pinned ? [pinned] : [],
            }), ct);
    }
}

public sealed partial class StartTrainingCommandHandler
{
    /// <summary>Label schema classes → names indexed by class id; training needs ids 0..N-1.</summary>
    internal static List<string> ClassNames(string classesJson)
    {
        var list = new List<(int Id, string Name)>();
        foreach (var n in JsonNode.Parse(classesJson) as JsonArray ?? [])
            if (n?["id"] is JsonValue id && id.TryGetValue<int>(out var i)) list.Add((i, n["name"]?.ToString() ?? $"class{i}"));
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        for (var k = 0; k < list.Count; k++)
            if (list[k].Id != k)
                throw new PreconditionException("TRACK_CLASS_IDS_GAP", "Training needs class ids 0..N-1 in the dataset's label schema");
        return list.Select(x => x.Name).ToList();
    }
}
