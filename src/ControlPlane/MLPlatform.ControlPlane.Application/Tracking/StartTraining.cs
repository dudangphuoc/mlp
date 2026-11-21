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

/// <summary>
/// Train from the UI (M09 §5.1): recipe + variant + task + params → run + job running `mlp-shim` in the recipe's image.
/// The shim spec is passed via the `MLP_JOB_SPEC` environment variable (JSON) — the shim reads it itself (20/08 §5.1).
/// </summary>
public sealed record StartTrainingCommand(Guid WorkspaceId, Guid ProjectId, string Name, string Recipe, string Variant, string Task,
    string ParamsJson, bool FromScratch, double Cpus, int Gpus, string? ImageOverride, string? Network, Guid? DatasetVersionId = null,
    string? ManifestOverride = null, Guid? HostId = null, TrialSpec? Trial = null, int GpuMemoryMb = 0) : ICommand<RunRow>;

public sealed partial class StartTrainingCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator, MLPlatform.Storage.S3Client s3)
    : ICommandHandler<StartTrainingCommand, RunRow>
{
    public async ValueTask<RunRow> Handle(StartTrainingCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var project = await db.GetProjectAsync(c.ProjectId, c.WorkspaceId, ct)
                      ?? throw new NotFoundException("TRACK_PROJECT_NOT_FOUND", "Project not found");
        var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, c.Recipe), ct);
        // Canvas path (D-49): the run cooks the manifest its revision froze, not whatever the Catalog holds today.
        var manifest = JsonNode.Parse(c.ManifestOverride ?? recipe.Manifest)!.AsObject();
        var task = string.IsNullOrEmpty(c.Task) ? project.Task : c.Task;
        if (!recipe.Tasks.Contains(task))
            throw new PreconditionException("CATALOG_TASK_UNSUPPORTED", $"Recipe {recipe.Name} does not support task {task}");
        var train = manifest["entrypoints"]?["train"] as JsonObject
                    ?? throw new PreconditionException("CATALOG_NO_TRAIN", $"Recipe {recipe.Name} has no train entrypoint");
        if (c.FromScratch && train["fromScratch"]?.GetValue<bool>() != true)
            throw new PreconditionException("CATALOG_FROM_SCRATCH_UNSUPPORTED", "Recipe does not support training from scratch (20/08 §4.2)");
        var variant = (manifest["variants"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(v => v["name"]?.ToString() == c.Variant)
                      ?? throw new NotFoundException("CATALOG_VARIANT_NOT_FOUND", $"Variant {c.Variant} not found");
        string image;
        if (!string.IsNullOrEmpty(c.ImageOverride)) image = c.ImageOverride;
        // Builtin recipes are built on the machine that runs the job (recipe Dockerfile + platform layer); only an invalid manifest is refused.
        else if (recipe.State != "invalid") image = $"recipes/{recipe.Name}:current";
        else throw new PreconditionException("CATALOG_RECIPE_NOT_VERIFIED", $"Recipe {recipe.Name} is not valid ({recipe.State})");

        JsonObject parameters;
        try { parameters = JsonNode.Parse(string.IsNullOrWhiteSpace(c.ParamsJson) ? "{}" : c.ParamsJson) as JsonObject ?? []; }
        catch (JsonException) { throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Invalid JSON" }); }
        var unknown = parameters.Select(p => p.Key).Where(k => manifest["params"]?[k] is null).ToList();
        if (unknown.Count > 0)
            throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Parameters not in the recipe: " + string.Join(", ", unknown) });
        var invalid = TrainParams.Validate(parameters, manifest["params"] as JsonObject ?? []);
        if (invalid.Count > 0)
            throw new InvalidArgumentException("TRACK_PARAMS_INVALID", invalid.First().Value, invalid);
        // A recipe that trains on data cannot start without a dataset version (dev/test image overrides are exempt: they bring their own data).
        if (c.DatasetVersionId is null && string.IsNullOrEmpty(c.ImageOverride) && TrainParams.NeedsData(manifest, task))
            throw new PreconditionException("TRACK_DATASET_REQUIRED", "Pick a dataset version — this recipe trains on data");
        if (c.Gpus < 0 || c.Gpus > 16)
            throw new InvalidArgumentException("TRACK_RESOURCES_INVALID", "GPUs must be between 0 and 16");
        if (c.Gpus == 0 && string.IsNullOrEmpty(c.ImageOverride) && manifest["environment"]?["gpu"]?.ToString() == "required")
            throw new PreconditionException("TRACK_GPU_REQUIRED", $"Recipe {recipe.Name} needs a GPU");

        var primary = manifest["metrics"]?["primary"]?.ToString() ?? "";
        if (primary.Contains('{')) primary = MLPlatform.Recipes.Template.Render(primary, (string n, out JsonNode? v) =>
        { v = n == "task" ? JsonValue.Create(task) : null; return n == "task"; }, MLPlatform.Recipes.RenderMode.Raw);

        // Data: dataset version (immutable) → presigned GET per file, shim downloads into {data} (20/08 §5.1 step 2)
        JsonArray? dataFiles = null;
        var datasetInfo = new JsonObject();
        var runId = Ids.New();
        string? dataFilesUrl = null;
        string? dataCache = null;   // host cache volume of the dataset, mounted by the scheduler (DataCacheRules)
        Guid? datasetId = null;
        if (c.DatasetVersionId is { } dvId)
        {
            var (version, dsTask, files) = await mediator.Query(new MLPlatform.ControlPlane.Application.Data.ResolveVersionFilesQuery(c.WorkspaceId, dvId), ct);
            if (c.Trial is { } subset) files = files.Where(f => subset.Files.Contains(f.Path)).ToList();
            if (dsTask != task) throw new PreconditionException("CATALOG_TASK_UNSUPPORTED", $"Dataset has task {dsTask}, but the project/recipe uses {task}");
            dataFiles = new JsonArray(files.Select(f => (JsonNode)f.ToJson()).ToArray());
            dataCache = MLPlatform.ControlPlane.Application.Data.DataCacheRules.Mount(version.DatasetId);
            datasetId = version.DatasetId;
            // An env var holds at most 128 KB → a long file list goes to storage and the shim reads it from a signed link.
            if (dataFiles.ToJsonString().Length > 64 * 1024)
            {
                var key = $"{c.WorkspaceId}/runs/{runId}/data-files.json";
                await s3.PutAsync(Buckets.Artifacts, key, System.Text.Encoding.UTF8.GetBytes(dataFiles.ToJsonString()), "application/json", ct);
                dataFilesUrl = s3.Presign(HttpMethod.Get, Buckets.Artifacts, key, TimeSpan.FromDays(7), MLPlatform.Storage.Audience.Workload);
                dataFiles = null;
            }
            // Classes from the dataset's label schema (ids 0..N-1) → {dataset.classes.*} and params declared `from: dataset.classes.count`.
            if (await db.GetCurrentSchemaAsync(version.DatasetId, ct) is { } schema)
            {
                var names = ClassNames(schema.Classes);
                datasetInfo["classes.count"] = names.Count;
                datasetInfo["classes.names_csv"] = string.Join(",", names);
                datasetInfo["classes.names_json"] = new JsonArray(names.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()).ToJsonString();
                foreach (var (k, def) in manifest["params"] as JsonObject ?? [])
                    if (def?["from"]?.ToString() == "dataset.classes.count" && !parameters.ContainsKey(k)) parameters[k] = names.Count;
            }
        }
        var weightsUrl = !c.FromScratch && variant["weights"]?.ToString() is { } wu && wu.StartsWith("https://", StringComparison.Ordinal) ? wu : null;
        // Spec for the shim; pretrained weights are provided by the Model Zoo (next P1)
        var shimSpec = new JsonObject
        {
            ["manifest"] = manifest.DeepClone(), ["entrypoint"] = "train", ["task"] = task, ["variant"] = variant.DeepClone(),
            ["params"] = parameters.DeepClone(), ["output"] = "/mnt/output", ["data"] = "/mnt/data",
            ["weights"] = c.FromScratch ? null : "/mnt/weights/" + (variant["name"]?.ToString() ?? "w"),
            ["weightsRef"] = c.FromScratch ? null : variant["weights"]?.ToString(),
            ["recipeSrc"] = "/opt/recipe/src",
            ["weightsUrl"] = weightsUrl,
            ["dataFiles"] = dataFiles,
            ["dataFilesUrl"] = dataFilesUrl,
            ["dataset"] = datasetInfo,
            ["hardware"] = new JsonObject { ["gpus_per_node"] = Math.Max(1, c.Gpus), ["device"] = c.Gpus > 0 ? "cuda" : "cpu" },
            ["datasetVersionId"] = c.DatasetVersionId?.ToString(),
            ["datasetId"] = datasetId?.ToString(),
            ["dataCache"] = dataCache,
        };
        if (c.Trial is { } t) shimSpec["trial"] = t.Passes.DeepClone();
        var number = (int)(c.Trial is null ? await db.NextRunNumberAsync(project.Id, c.WorkspaceId, ct) : await db.NextTrialRunNumberAsync(project.Id, c.WorkspaceId, ct));
        var name = string.IsNullOrWhiteSpace(c.Name) ? c.Trial is null ? $"{c.Variant}-{number}" : $"trial-{c.Variant}" : c.Name.Trim();
        if (name.Length > 80) throw new InvalidArgumentException("TRACK_NAME_INVALID", "Run name must be at most 80 characters");
        var env = new Dictionary<string, string> { ["MLP_JOB_SPEC"] = shimSpec.ToJsonString() };
        if (manifest["environment"]?["shmSizeGb"] is JsonValue shm && shm.TryGetValue<int>(out var shmGb) && shmGb > 0) env["MLP_SHM_GB"] = shmGb.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "train", name,
            new JobSpecDto
            {
                Image = image, Command = ["mlp-shim", "run", "--spec-env", "MLP_JOB_SPEC"], Cpus = c.Cpus <= 0 ? 1 : c.Cpus, Gpus = c.Gpus,
                GpuMemoryMb = c.GpuMemoryMb, Env = env, Network = c.Network, Hosts = c.HostId is { } pinned ? [pinned] : [],
            }), ct);
        var now = clock.UtcNow;
        await db.InsertRunAsync(runId, project.Id, c.WorkspaceId, name, jobId, uid,
            recipe.Name, c.Variant, task, parameters.ToJsonString(), primary, now, number, c.DatasetVersionId, c.FromScratch, c.Gpus,
            c.Cpus <= 0 ? 1 : c.Cpus, ct);
        if (c.Trial is not null) await db.MarkRunTrialAsync(runId, c.WorkspaceId, ct);
        return (await db.GetRunAsync(runId, ct))!;
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
