using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tuning;

public sealed record SweepRow(Guid Id, Guid WorkspaceId, Guid ProjectId, string Name, string Recipe, string Variant, string Task, string Algorithm,
    string Space, string BaseParams, string Objective, string Direction, int MaxTrials, int Parallel, int Seed, double Cpus, int Gpus,
    Guid? DatasetVersionId, string? ImageOverride, string? Network, string State, string? StateReason, int? BestTrial, double? BestValue,
    Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);
public sealed record TrialRow(int Number, string Params, Guid? RunId, string State, double? Value, string? Error, DateTimeOffset CreatedAt, Guid? JobId);

public static partial class TuneSql
{
    [SqlQuery("Tune/InsertSweep.sql")]
    public static partial Task InsertSweepAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid projectId, string name, string recipe, string variant,
        string task, string algorithm, string space, string baseParams, string objective, string direction, int maxTrials, int parallel, int seed,
        double cpus, int gpus, Guid? datasetVersionId, string? imageOverride, string? network, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Tune/GetSweep.sql")]
    public static partial Task<SweepRow?> GetSweepAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Tune/ListSweeps.sql")]
    public static partial Task<List<SweepRow>> ListSweepsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Tune/ListRunningSweeps.sql")]
    public static partial Task<List<SweepRow>> ListRunningSweepsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Tune/FinishSweep.sql")]
    public static partial Task FinishSweepAsync(this IWriteSession db, Guid id, string state, string? reason, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Tune/SetBest.sql")]
    public static partial Task SetBestAsync(this IWriteSession db, Guid id, int? bestTrial, double? bestValue, CancellationToken ct);
    [SqlQuery("Tune/InsertTrial.sql")]
    public static partial Task InsertTrialAsync(this IWriteSession db, Guid sweepId, int number, string trialParams, Guid? runId, string state, string? error,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Tune/ListTrials.sql")]
    public static partial Task<List<TrialRow>> ListTrialsAsync(this IReadSession db, Guid sweepId, CancellationToken ct);
    [SqlQuery("Tune/SyncTrials.sql")]
    public static partial Task SyncTrialsAsync(this IWriteSession db, Guid sweepId, string objective, CancellationToken ct);
}

/// <summary>
/// Search space (M10 §2): each parameter is `{"values":[…]}` (discrete; grid enumerates all) or `{"min":a,"max":b,"log":bool,"int":bool}` (continuous; random only).
/// Parameters are generated deterministically from (seed, trial number) → rerunning a sweep with the same seed yields the same trial sequence.
/// </summary>
public sealed class SearchSpace
{
    private sealed record Dim(string Name, JsonNode?[]? Values, double Min, double Max, bool Log, bool Int);
    private readonly List<Dim> _dims;

    private SearchSpace(List<Dim> dims) => _dims = dims;

    public static SearchSpace Parse(string json, Func<string, bool> knownParam)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw Bad("Search space must be an object"); }
        catch (JsonException) { throw Bad("Invalid JSON"); }
        if (root.Count == 0) throw Bad("At least one parameter is required");
        var dims = new List<Dim>();
        foreach (var (name, node) in root)
        {
            if (!knownParam(name)) throw Bad($"Parameter not in the recipe: {name}");
            if (node is not JsonObject o) throw Bad($"{name}: requires {{values}} or {{min,max}}");
            if (o["values"] is JsonArray arr)
            {
                if (arr.Count == 0) throw Bad($"{name}: values is empty");
                dims.Add(new Dim(name, arr.Select(v => v?.DeepClone()).ToArray(), 0, 0, false, false));
            }
            else
            {
                double Num(string k) => o[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : throw Bad($"{name}: missing {k}");
                var (min, max) = (Num("min"), Num("max"));
                var log = o["log"]?.GetValue<bool>() == true;
                if (max <= min || (log && min <= 0)) throw Bad($"{name}: invalid range");
                dims.Add(new Dim(name, null, min, max, log, o["int"]?.GetValue<bool>() == true));
            }
        }
        return new SearchSpace(dims);
    }

    private static ValidationException Bad(string msg) => new(new Dictionary<string, string> { ["space"] = msg });

    /// <summary>Number of grid combinations; null when there is a continuous dimension (grid not applicable).</summary>
    public long? GridSize => _dims.All(d => d.Values is not null) ? _dims.Aggregate(1L, (n, d) => n * d.Values!.Length) : null;

    public JsonObject Grid(int index)
    {
        var o = new JsonObject();
        foreach (var d in Enumerable.Reverse(_dims))
        {
            o[d.Name] = d.Values![index % d.Values.Length]?.DeepClone();
            index /= d.Values.Length;
        }
        return Order(o);
    }

    public JsonObject Random(int seed, int number)
    {
        var rng = new Random(unchecked(seed * 7919 + number));
        var o = new JsonObject();
        foreach (var d in _dims)
        {
            if (d.Values is not null) { o[d.Name] = d.Values[rng.Next(d.Values.Length)]?.DeepClone(); continue; }
            var u = rng.NextDouble();
            var x = d.Log ? Math.Exp(Math.Log(d.Min) + u * (Math.Log(d.Max) - Math.Log(d.Min))) : d.Min + u * (d.Max - d.Min);
            o[d.Name] = d.Int ? JsonValue.Create((long)Math.Round(x)) : JsonValue.Create(Math.Round(x, 6));
        }
        return o;
    }

    private JsonObject Order(JsonObject o)
    {
        var r = new JsonObject();
        foreach (var d in _dims) r[d.Name] = o[d.Name]?.DeepClone();
        return r;
    }
}

public sealed record CreateSweepCommand(Guid WorkspaceId, Guid ProjectId, string Name, string Recipe, string Variant, string Task, string Algorithm,
    string SpaceJson, string BaseParamsJson, string Objective, string Direction, int MaxTrials, int Parallel, int Seed, double Cpus, int Gpus,
    Guid? DatasetVersionId, string? ImageOverride, string? Network) : ICommand<SweepRow>;

public sealed class CreateSweepCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<CreateSweepCommand, SweepRow>
{
    public async ValueTask<SweepRow> Handle(CreateSweepCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var project = await db.GetProjectAsync(c.ProjectId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_PROJECT_NOT_FOUND", "Project not found");
        var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, c.Recipe), ct);
        var manifest = JsonNode.Parse(recipe.Manifest)!.AsObject();
        var space = SearchSpace.Parse(c.SpaceJson, k => manifest["params"]?[k] is not null);
        var algorithm = string.IsNullOrEmpty(c.Algorithm) ? "random" : c.Algorithm;
        if (algorithm is not ("grid" or "random")) throw new InvalidArgumentException("TUNE_ALGORITHM_UNSUPPORTED", "Available algorithms: grid, random (Bayesian/ASHA later)");
        if (algorithm == "grid" && space.GridSize is null) throw new InvalidArgumentException("TUNE_GRID_NEEDS_VALUES", "Grid requires every parameter in the form {values:[…]}");
        var maxTrials = c.MaxTrials is > 0 and <= 500 ? c.MaxTrials : 10;
        if (algorithm == "grid") maxTrials = (int)Math.Min(maxTrials, space.GridSize!.Value);
        var direction = string.IsNullOrEmpty(c.Direction) ? "maximize" : c.Direction;
        if (direction is not ("maximize" or "minimize")) throw new ValidationException(new Dictionary<string, string> { ["direction"] = "maximize | minimize" });
        var objective = string.IsNullOrWhiteSpace(c.Objective) ? manifest["metrics"]?["primary"]?.ToString() ?? "" : c.Objective.Trim();
        if (objective.Length == 0 || objective.Contains('{'))
            throw new ValidationException(new Dictionary<string, string> { ["objective"] = "An objective metric must be specified" });
        try { _ = JsonNode.Parse(string.IsNullOrWhiteSpace(c.BaseParamsJson) ? "{}" : c.BaseParamsJson) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new ValidationException(new Dictionary<string, string> { ["base_params"] = "Invalid JSON" }); }
        var id = Ids.New();
        await db.InsertSweepAsync(id, c.WorkspaceId, c.ProjectId, string.IsNullOrWhiteSpace(c.Name) ? $"sweep-{recipe.Name}" : c.Name.Trim(),
            recipe.Name, c.Variant, string.IsNullOrEmpty(c.Task) ? project.Task : c.Task, algorithm, c.SpaceJson,
            string.IsNullOrWhiteSpace(c.BaseParamsJson) ? "{}" : c.BaseParamsJson, objective, direction, maxTrials,
            c.Parallel is > 0 and <= 16 ? c.Parallel : 2, c.Seed, c.Cpus <= 0 ? 1 : c.Cpus, Math.Max(0, c.Gpus), c.DatasetVersionId,
            string.IsNullOrEmpty(c.ImageOverride) ? null : c.ImageOverride, string.IsNullOrEmpty(c.Network) ? null : c.Network, uid, clock.UtcNow, ct);
        return (await db.GetSweepAsync(id, c.WorkspaceId, ct))!;
    }
}

public sealed record ListSweepsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<SweepRow>>;

public sealed class ListSweepsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListSweepsQuery, IReadOnlyList<SweepRow>>
{
    public async ValueTask<IReadOnlyList<SweepRow>> Handle(ListSweepsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListSweepsAsync(q.WorkspaceId, ct);
    }
}

public sealed record GetSweepQuery(Guid WorkspaceId, Guid SweepId) : IQuery<(SweepRow Sweep, IReadOnlyList<TrialRow> Trials)>;

public sealed class GetSweepQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetSweepQuery, (SweepRow Sweep, IReadOnlyList<TrialRow> Trials)>
{
    public async ValueTask<(SweepRow Sweep, IReadOnlyList<TrialRow> Trials)> Handle(GetSweepQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var s = await db.GetSweepAsync(q.SweepId, q.WorkspaceId, ct) ?? throw new NotFoundException("TUNE_SWEEP_NOT_FOUND", "Sweep not found");
        return (s, await db.ListTrialsAsync(s.Id, ct));
    }
}

/// <summary>Stop a sweep: no new trials are generated; returns the running trials' jobs so the host stops the containers (WorkloadLauncher.StopJobAsync).</summary>
public sealed record StopSweepCommand(Guid WorkspaceId, Guid SweepId) : ICommand<(SweepRow Sweep, IReadOnlyList<Guid> ActiveJobs)>;

public sealed class StopSweepCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<StopSweepCommand, (SweepRow Sweep, IReadOnlyList<Guid> ActiveJobs)>
{
    public async ValueTask<(SweepRow Sweep, IReadOnlyList<Guid> ActiveJobs)> Handle(StopSweepCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var s = await db.GetSweepAsync(c.SweepId, c.WorkspaceId, ct) ?? throw new NotFoundException("TUNE_SWEEP_NOT_FOUND", "Sweep not found");
        if (s.State != "running") return (s, []);
        await db.FinishSweepAsync(s.Id, "stopped", "Stopped by user", clock.UtcNow, ct);
        var active = (await db.ListTrialsAsync(s.Id, ct)).Where(t => t.State is "queued" or "running" && t.JobId is not null).Select(t => t.JobId!.Value).ToList();
        return ((await db.GetSweepAsync(s.Id, c.WorkspaceId, ct))!, active);
    }
}

/// <summary>
/// One orchestrator step (M10 §5): sync trial state/values from M09 runs → update the best trial → done when the budget is used up and every
/// trial has finished → if there is room (parallel), generate new trials via StartTraining on behalf of the sweep creator (the host sets the user for the scope).
/// </summary>
public sealed record AdvanceSweepCommand(Guid WorkspaceId, Guid SweepId) : ICommand<int>, IInternalCommand;

public sealed class AdvanceSweepCommandHandler(IWriteSession db, IClock clock, IMediator mediator) : ICommandHandler<AdvanceSweepCommand, int>
{
    public async ValueTask<int> Handle(AdvanceSweepCommand c, CancellationToken ct)
    {
        var s = await db.GetSweepAsync(c.SweepId, c.WorkspaceId, ct);
        if (s is null || s.State != "running") return 0;
        await db.SyncTrialsAsync(s.Id, s.Objective, ct);
        var trials = await db.ListTrialsAsync(s.Id, ct);
        var scored = trials.Where(t => t.State == "succeeded" && t.Value is not null).ToList();
        var best = s.Direction == "maximize" ? scored.MaxBy(t => t.Value) : scored.MinBy(t => t.Value);
        if (best is not null && (best.Number != s.BestTrial || best.Value != s.BestValue)) await db.SetBestAsync(s.Id, best.Number, best.Value, ct);

        var active = trials.Count(t => t.State is "queued" or "running");
        var failed = trials.Count(t => t.State is "failed" or "cancelled");
        if (trials.Count >= s.MaxTrials && active == 0)
        {
            var ok = best is not null;
            await db.FinishSweepAsync(s.Id, ok ? "completed" : "failed", ok ? null : "No trial produced an objective value", clock.UtcNow, ct);
            await Notifier.SendAsync(db, clock, s.WorkspaceId, [s.CreatedBy], ok ? "sweep.completed" : "sweep.failed",
                ok ? $"Sweep \"{s.Name}\" finished — best is trial #{best!.Number} ({s.Objective} = {best.Value!.Value.ToString("0.####", CultureInfo.InvariantCulture)})"
                   : $"Sweep \"{s.Name}\" failed", $"#/sweeps/{s.Id}", $"sweep/{s.Id}", ok ? "success" : "error", ct);
            return 0;
        }
        // Repeated failures right from the start (e.g. wrong recipe/params) → stop early instead of burning the whole budget.
        if (failed >= 3 && scored.Count == 0 && trials.Count == failed)
        {
            await db.FinishSweepAsync(s.Id, "failed", "The first 3 trials all failed", clock.UtcNow, ct);
            await Notifier.SendAsync(db, clock, s.WorkspaceId, [s.CreatedBy], "sweep.failed", $"Sweep \"{s.Name}\" failed (first 3 trials failed)",
                $"#/sweeps/{s.Id}", $"sweep/{s.Id}", "error", ct);
            return 0;
        }
        var recipe = await mediator.Query(new GetRecipeQuery(s.WorkspaceId, s.Recipe), ct);
        var manifest = JsonNode.Parse(recipe.Manifest)!.AsObject();
        var space = SearchSpace.Parse(s.Space, k => manifest["params"]?[k] is not null);
        var launched = 0;
        for (var n = trials.Count; n < s.MaxTrials && active < s.Parallel; n++, active++)
        {
            var p = JsonNode.Parse(s.BaseParams) as JsonObject ?? [];
            foreach (var (k, v) in s.Algorithm == "grid" ? space.Grid(n) : space.Random(s.Seed, n)) p[k] = v?.DeepClone();
            var paramsJson = p.ToJsonString();
            try
            {
                var run = await mediator.Send(new StartTrainingCommand(s.WorkspaceId, s.ProjectId, $"{s.Name}-t{n}", s.Recipe, s.Variant, s.Task,
                    paramsJson, false, s.Cpus, s.Gpus, s.ImageOverride, s.Network, s.DatasetVersionId), ct);
                await db.InsertTrialAsync(s.Id, n, paramsJson, run.Id, "queued", null, clock.UtcNow, ct);
            }
            catch (AppException e)
            {
                await db.InsertTrialAsync(s.Id, n, paramsJson, null, "failed", $"{e.Reason}: {e.Message}", clock.UtcNow, ct);
                active--;
            }
            launched++;
        }
        return launched;
    }
}

public sealed record ListRunningSweepsQuery : IQuery<IReadOnlyList<SweepRow>>;

public sealed class ListRunningSweepsQueryHandler(IReadSession db) : IQueryHandler<ListRunningSweepsQuery, IReadOnlyList<SweepRow>>
{
    public async ValueTask<IReadOnlyList<SweepRow>> Handle(ListRunningSweepsQuery q, CancellationToken ct) => await db.ListRunningSweepsAsync(ct);
}
