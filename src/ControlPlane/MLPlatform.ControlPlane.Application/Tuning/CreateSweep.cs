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

[RequiresPermission("job.submit")]
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
