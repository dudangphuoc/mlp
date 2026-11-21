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

public sealed record CreateProjectCommand(Guid WorkspaceId, string Slug, string Name, string Task) : ICommand<ProjectRow>;

public sealed class CreateProjectCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateProjectCommand, ProjectRow>
{
    public async ValueTask<ProjectRow> Handle(CreateProjectCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        if (!IdentityRules.Slug().IsMatch(c.Slug))
            throw new ValidationException(new Dictionary<string, string> { ["slug"] = "Invalid slug" });
        var id = Ids.New();
        try
        {
            await db.InsertProjectAsync(id, c.WorkspaceId, c.Slug, c.Name, c.Task, uid, clock.UtcNow, ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        {
            throw new ConflictException("TRACK_PROJECT_EXISTS", "Project URL is already in use");
        }
        return new ProjectRow(id, c.WorkspaceId, c.Slug, c.Name, c.Task, clock.UtcNow);
    }
}

public sealed record ListProjectsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ProjectRow>>;

public sealed class ListProjectsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectRow>>
{
    public async ValueTask<IReadOnlyList<ProjectRow>> Handle(ListProjectsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListProjectsAsync(q.WorkspaceId, ct);
    }
}

/// <summary>Run counts and best result per project of a workspace (project list cards).</summary>
public sealed record ListProjectStatsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ProjectStatsRow>>;

/// <summary>Reads the per-project statistics after the workspace permission check.</summary>
public sealed class ListProjectStatsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListProjectStatsQuery, IReadOnlyList<ProjectStatsRow>>
{
    public async ValueTask<IReadOnlyList<ProjectStatsRow>> Handle(ListProjectStatsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ProjectStatsAsync(q.WorkspaceId, ct);
    }
}

/// <summary>
/// Train from the UI (M09 §5.1): recipe + variant + task + params → run + job running `mlp-shim` in the recipe's image.
/// The shim spec is passed via the `MLP_JOB_SPEC` environment variable (JSON) — the shim reads it itself (20/08 §5.1).
/// </summary>
public sealed record StartTrainingCommand(Guid WorkspaceId, Guid ProjectId, string Name, string Recipe, string Variant, string Task,
    string ParamsJson, bool FromScratch, double Cpus, int Gpus, string? ImageOverride, string? Network, Guid? DatasetVersionId = null,
    string? ManifestOverride = null, Guid? HostId = null, TrialSpec? Trial = null, int GpuMemoryMb = 0) : ICommand<RunRow>;

/// <summary>
/// Trial mode of <see cref="StartTrainingCommand"/> (round 1 R15–R22): only <paramref name="Files"/> of the dataset version are handed to the shim,
/// the run is hidden (numbered -1, -2, …) and the shim runs its two short passes with <paramref name="Passes"/> (<c>entrypoints.train.trial</c>).
/// </summary>
public sealed record TrialSpec(IReadOnlySet<string> Files, JsonObject Passes);

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

public sealed record ListRunsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<RunRow>>;

public sealed class ListRunsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListRunsQuery, IReadOnlyList<RunRow>>
{
    public async ValueTask<IReadOnlyList<RunRow>> Handle(ListRunsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListRunsAsync(q.ProjectId, q.WorkspaceId, ct);
    }
}

public sealed record GetRunQuery(Guid WorkspaceId, Guid RunId) : IQuery<RunRow>;

public sealed class GetRunQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetRunQuery, RunRow>
{
    public async ValueTask<RunRow> Handle(GetRunQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var r = await db.GetRunAsync(q.RunId, ct);
        return r is not null && r.WorkspaceId == q.WorkspaceId ? r : throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
    }
}

/// <summary>Log metrics: a user with run write permission, or the container of the same job (workload token).</summary>
public sealed record LogMetricsCommand(Guid RunId, string? WorkloadToken, IReadOnlyList<(string Key, long Step, double Value, DateTimeOffset? At)> Points)
    : ICommand<int>;

public sealed class LogMetricsCommandHandler(IWriteSession db, IClock clock, TokenService tokens, WorkspaceAuthorizer auth)
    : ICommandHandler<LogMetricsCommand, int>
{
    public async ValueTask<int> Handle(LogMetricsCommand c, CancellationToken ct)
    {
        var run = await db.GetRunAsync(c.RunId, ct) ?? throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        if (c.WorkloadToken is { Length: > 0 } t)
        {
            var a = await MLPlatform.ControlPlane.Application.Ops.Workload.ResolveAsync(db, tokens, t, ct);   // fencing (20/10 §4.5)
            if (a.JobId != run.JobId) throw new UnauthenticatedException("SCHED_TOKEN_INVALID", "Job token is not valid for this run");
        }
        else await auth.RequireAsync(run.WorkspaceId, "job.submit", ct);
        if (c.Points.Count > 10_000) throw new ResourceExhaustedException("TRACK_BATCH_TOO_LARGE", "At most 10,000 points per batch");
        double? lastPrimary = null;
        long lastStep = long.MinValue;
        foreach (var p in c.Points)
        {
            if (!double.IsFinite(p.Value)) continue;
            await db.InsertMetricAsync(run.Id, p.Key, p.Step, p.At ?? clock.UtcNow, p.Value, ct);
            if (p.Key == run.PrimaryMetricKey && p.Step >= lastStep) { lastPrimary = p.Value; lastStep = p.Step; }
        }
        if (lastPrimary is { } v) await db.SetPrimaryMetricAsync(run.Id, v, ct);
        if (run.State == "queued") await db.SetRunStateAsync(run.Id, "running", clock.UtcNow, ct);
        return c.Points.Count;
    }
}

public sealed record GetMetricsQuery(Guid WorkspaceId, Guid RunId, IReadOnlyList<string>? Keys = null, int MaxPoints = 0) : IQuery<IReadOnlyList<MetricRow>>;

public sealed class GetMetricsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetMetricsQuery, IReadOnlyList<MetricRow>>
{
    public async ValueTask<IReadOnlyList<MetricRow>> Handle(GetMetricsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var r = await db.GetRunAsync(q.RunId, ct);
        if (r is null || r.WorkspaceId != q.WorkspaceId) throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        var keys = q.Keys?.Where(k => k.Length > 0).Distinct().ToArray() ?? [];
        return await db.GetMetricsSampledAsync(q.RunId, keys.Length > 0, keys, q.MaxPoints is > 0 and <= 5000 ? q.MaxPoints : 600, ct);
    }
}

/// <summary>Every metric of a run without its points — the run page lists them and loads charts on demand.</summary>
public sealed record GetMetricSummaryQuery(Guid WorkspaceId, Guid RunId) : IQuery<IReadOnlyList<MetricSummaryRow>>;

/// <summary>Reads the per-metric summary after checking the run belongs to the workspace.</summary>
public sealed class GetMetricSummaryQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetMetricSummaryQuery, IReadOnlyList<MetricSummaryRow>>
{
    public async ValueTask<IReadOnlyList<MetricSummaryRow>> Handle(GetMetricSummaryQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var r = await db.GetRunAsync(q.RunId, ct);
        if (r is null || r.WorkspaceId != q.WorkspaceId) throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        return await db.MetricSummaryAsync(q.RunId, ct);
    }
}

/// <summary>A run with what other modules know about it: its job (state, reason, progress), the dataset version it trained on, who started it.</summary>
public sealed record RunView(RunRow Run, JobInfoRow? Job, VersionNameRow? Dataset, string? UserName, RunExtraRow? Extra = null);

/// <summary>Builds run views from run rows plus the job, dataset and user lookups.</summary>
public static class RunViews
{
    /// <summary>One lookup per module for the whole list — never one per run.</summary>
    public static async Task<List<RunView>> BuildAsync(IReadSession db, IReadOnlyList<RunRow> runs, CancellationToken ct)
    {
        if (runs.Count == 0) return [];
        var ws = runs[0].WorkspaceId;
        var jobs = (await db.JobsByIdsAsync(ws, runs.Where(r => r.JobId is not null).Select(r => r.JobId!.Value).Distinct().ToArray(), ct)).ToDictionary(j => j.Id);
        var versionIds = runs.Where(r => r.DatasetVersionId is not null).Select(r => r.DatasetVersionId!.Value).Distinct().ToArray();
        var versions = versionIds.Length == 0 ? [] : (await db.VersionNamesAsync(ws, versionIds, ct)).ToDictionary(v => v.Id);
        var users = (await db.UserNamesAsync(runs.Select(r => r.UserId).Distinct().ToArray(), ct)).ToDictionary(u => u.Id, u => u.DisplayName);
        var extras = (await db.RunExtrasAsync(ws, runs.Select(r => r.Id).ToArray(), ct)).ToDictionary(e => e.RunId);
        return runs.Select(r => new RunView(r,
            r.JobId is { } j ? jobs.GetValueOrDefault(j) : null,
            r.DatasetVersionId is { } v ? versions.GetValueOrDefault(v) : null,
            users.GetValueOrDefault(r.UserId), extras.GetValueOrDefault(r.Id))).ToList();
    }
}

/// <summary>Runs of a project as the run list shows them.</summary>
public sealed record ListRunViewsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<RunView>>;

/// <summary>Lists the project runs and attaches job, dataset and user details.</summary>
public sealed class ListRunViewsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListRunViewsQuery, IReadOnlyList<RunView>>
{
    public async ValueTask<IReadOnlyList<RunView>> Handle(ListRunViewsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await RunViews.BuildAsync(db, await db.ListRunsAsync(q.ProjectId, q.WorkspaceId, ct), ct);
    }
}

/// <summary>One run as the run page shows it.</summary>
public sealed record GetRunViewQuery(Guid WorkspaceId, Guid RunId) : IQuery<RunView>;

/// <summary>Reads one run and attaches job, dataset and user details.</summary>
public sealed class GetRunViewQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetRunViewQuery, RunView>
{
    public async ValueTask<RunView> Handle(GetRunViewQuery q, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, q.WorkspaceId, q.RunId, "run.read", ct);
        return (await RunViews.BuildAsync(db, [r], ct))[0];
    }
}

/// <summary>Rules shared by run commands: what counts as finished, and the workspace check every command starts with.</summary>
public static class RunRules
{
    public static bool Finished(string state) => state is "succeeded" or "failed" or "cancelled";

    internal static async Task<RunRow> RequireAsync(IReadSession db, WorkspaceAuthorizer auth, Guid ws, Guid runId, string permission, CancellationToken ct)
    {
        await auth.RequireAsync(ws, permission, ct);
        var r = await db.GetRunAsync(runId, ct);
        return r is not null && r.WorkspaceId == ws ? r : throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
    }
}

/// <summary>Stop a run that is waiting or training: marks it cancelled and returns its job so the host stops the container (WorkloadLauncher.StopJobAsync).</summary>
public sealed record StopRunCommand(Guid WorkspaceId, Guid RunId) : ICommand<Guid?>;

/// <summary>Marks the run cancelled; the host then stops the job.</summary>
public sealed class StopRunCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<StopRunCommand, Guid?>
{
    public async ValueTask<Guid?> Handle(StopRunCommand c, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, "job.submit", ct);
        if (RunRules.Finished(r.State)) throw new PreconditionException("TRACK_RUN_FINISHED", "This run has already finished");
        await db.SetRunStateAsync(r.Id, "cancelled", clock.UtcNow, ct);
        return r.JobId;
    }
}

/// <summary>Rename a run (1–80 characters).</summary>
public sealed record RenameRunCommand(Guid WorkspaceId, Guid RunId, string Name) : ICommand<bool>;

/// <summary>Validates the name and renames the run.</summary>
public sealed class RenameRunCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<RenameRunCommand, bool>
{
    public async ValueTask<bool> Handle(RenameRunCommand c, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, "job.submit", ct);
        var name = c.Name.Trim();
        if (name.Length is 0 or > 80) throw new InvalidArgumentException("TRACK_NAME_INVALID", "Run name must be 1 to 80 characters");
        await db.RenameRunAsync(r.Id, r.WorkspaceId, name, ct);
        return true;
    }
}

/// <summary>
/// Delete a finished run with its metrics and file list. Refused while a registered model version still points at it —
/// that version is the record of where the model came from. Returns the storage prefix so the host removes the files.
/// </summary>
public sealed record DeleteRunCommand(Guid WorkspaceId, Guid RunId) : ICommand<string>;

/// <summary>Deletes the run records after the finished / no-registered-model checks.</summary>
public sealed class DeleteRunCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteRunCommand, string>
{
    public async ValueTask<string> Handle(DeleteRunCommand c, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, "job.submit", ct);
        if (!RunRules.Finished(r.State)) throw new PreconditionException("TRACK_RUN_ACTIVE", "Stop the run before deleting it");
        if (await MLPlatform.ControlPlane.Application.Registry.RegistrySql.CountVersionsByRunAsync(db, r.Id, ct) > 0)
            throw new PreconditionException("TRACK_RUN_HAS_MODEL", "A registered model version was created from this run — delete that version first");
        await db.DeleteRunAsync(r.Id, r.WorkspaceId, ct);
        return RunArtifacts.Prefix(r.WorkspaceId, r.Id);
    }
}

/// <summary>A file of the run's model directory with a time-limited download link.</summary>
public sealed record RunFile(string Path, long SizeBytes, string Url);

/// <summary>Files of the run's model directory.</summary>
public sealed record ListRunFilesQuery(Guid WorkspaceId, Guid RunId) : IQuery<IReadOnlyList<RunFile>>;

/// <summary>Lists the uploaded model files and signs a download link for each.</summary>
public sealed class ListRunFilesQueryHandler(IReadSession db, WorkspaceAuthorizer auth, MLPlatform.Storage.S3Client s3) : IQueryHandler<ListRunFilesQuery, IReadOnlyList<RunFile>>
{
    public async ValueTask<IReadOnlyList<RunFile>> Handle(ListRunFilesQuery q, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, q.WorkspaceId, q.RunId, "run.read", ct);
        var prefix = RunArtifacts.Prefix(r.WorkspaceId, r.Id);
        return (await db.ListRunArtifactsAsync(r.Id, ct))
            .Select(f => new RunFile(f.Path, f.SizeBytes, s3.Presign(HttpMethod.Get, Buckets.Artifacts, prefix + f.Path, TimeSpan.FromHours(1))))
            .ToList();
    }
}

/// <summary>Checks training parameters against the recipe's schema (type, minimum, maximum, multipleOf, enum) before anything is queued.</summary>
public static class TrainParams
{
    public static Dictionary<string, string> Validate(JsonObject values, JsonObject schema)
    {
        var errors = new Dictionary<string, string>();
        foreach (var (key, node) in values)
        {
            if (schema[key] is not JsonObject def) continue;
            if (Check(key, node, def) is { } e) errors["params." + key] = e;
        }
        return errors;
    }

    private static string? Check(string key, JsonNode? node, JsonObject def)
    {
        var type = def["type"] is JsonValue tv ? tv.ToString() : null;
        if (node is not JsonValue v) return $"Parameter {key}: a single value is expected";
        if (def["enum"] is JsonArray options)
        {
            var text = v.ToString();
            return options.Any(o => o?.ToString() == text) ? null : $"Parameter {key}: must be one of {string.Join(", ", options.Select(o => o?.ToString()))}";
        }
        switch (type)
        {
            case "boolean":
                return v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? null : $"Parameter {key}: must be true or false";
            case "string":
                return v.GetValueKind() == JsonValueKind.String ? null : $"Parameter {key}: must be text";
            case "integer" or "number":
                if (v.GetValueKind() != JsonValueKind.Number || !v.TryGetValue<double>(out var n) || !double.IsFinite(n)) return $"Parameter {key}: must be a number";
                if (type == "integer" && n != Math.Floor(n)) return $"Parameter {key}: must be a whole number";
                if (Num(def, "minimum") is { } lo && n < lo) return $"Parameter {key}: must be at least {Text(lo)}";
                if (Num(def, "exclusiveMinimum") is { } xlo && n <= xlo) return $"Parameter {key}: must be greater than {Text(xlo)}";
                if (Num(def, "maximum") is { } hi && n > hi) return $"Parameter {key}: must be at most {Text(hi)}";
                if (Num(def, "multipleOf") is { } m && m > 0 && Math.Abs(n / m - Math.Round(n / m)) > 1e-9) return $"Parameter {key}: must be a multiple of {Text(m)}";
                return null;
            default:
                return null;
        }
    }

    private static double? Num(JsonObject def, string name) => def[name] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
    private static string Text(double d) => d.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>`data.input` is a format name, or a map task → format; "none" (or absent) means the recipe brings no training data.</summary>
    public static bool NeedsData(JsonObject manifest, string task)
    {
        var input = manifest["data"]?["input"];
        var format = input is JsonObject byTask ? byTask[task]?.ToString() : input?.ToString();
        return !string.IsNullOrEmpty(format) && format != "none";
    }
}

/// <summary>Job finished → run state (called from the api after FinishAttempt; to be replaced by a domain event once there is an outbox).</summary>
public sealed record SyncRunStateCommand(Guid JobId, string JobState) : ICommand<bool>;

public sealed class SyncRunStateCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SyncRunStateCommand, bool>
{
    public async ValueTask<bool> Handle(SyncRunStateCommand c, CancellationToken ct)
    {
        if (await db.GetRunByJobAsync(c.JobId, ct) is not { } r) return false;
        await db.SetRunStateAsync(r.Id, c.JobState == "queued" ? "queued" : c.JobState, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>Issues a token to the container at launch (the shim uses it to log metrics/progress).</summary>
public sealed record MintWorkloadTokenCommand(Guid AttemptId) : ICommand<string>;

public sealed class MintWorkloadTokenCommandHandler(IWriteSession db, TokenService tokens) : ICommandHandler<MintWorkloadTokenCommand, string>
{
    public async ValueTask<string> Handle(MintWorkloadTokenCommand c, CancellationToken ct)
    {
        var t = TokenService.New("mlpj_");
        await db.SetWorkloadTokenAsync(c.AttemptId, tokens.Hash(t), ct);
        return t;
    }
}

public sealed record GetRunIdForJobQuery(Guid JobId) : IQuery<Guid?>;

public sealed class GetRunIdForJobQueryHandler(IReadSession db) : IQueryHandler<GetRunIdForJobQuery, Guid?>
{
    public async ValueTask<Guid?> Handle(GetRunIdForJobQuery q, CancellationToken ct) => (await db.GetRunByJobAsync(q.JobId, ct))?.Id;
}
