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

/// <summary>One orchestrator step (M10 §5): sync trial state/values from M09 runs → update the best trial → done when
/// the budget is used up and every trial has finished → if there is room (parallel).</summary>
[Access(AccessKind.Internal)]
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
            await FinishWhenBudgetUsedAsync(s, best, ct);
            return 0;
        }
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
            if (!await LaunchTrialAsync(s, space, n, ct)) active--;
            launched++;
        }
        return launched;
    }

    private async Task FinishWhenBudgetUsedAsync(SweepRow s, TrialRow? best, CancellationToken ct)
    {
        var ok = best is not null;
        await db.FinishSweepAsync(s.Id, ok ? "completed" : "failed", ok ? null : "No trial produced an objective value", clock.UtcNow, ct);
        await Notifier.SendAsync(db, clock, s.WorkspaceId, [s.CreatedBy], ok ? "sweep.completed" : "sweep.failed",
            ok ? $"Sweep \"{s.Name}\" finished — best is trial #{best!.Number} ({s.Objective} = {best.Value!.Value.ToString("0.####", CultureInfo.InvariantCulture)})"
               : $"Sweep \"{s.Name}\" failed", $"#/sweeps/{s.Id}", $"sweep/{s.Id}", ok ? "success" : "error", ct);
    }

    /// <summary>Starts trial n; false when the start failed and the trial was recorded as failed.</summary>
    private async Task<bool> LaunchTrialAsync(SweepRow s, SearchSpace space, int n, CancellationToken ct)
    {
        var p = JsonNode.Parse(s.BaseParams) as JsonObject ?? [];
        foreach (var (k, v) in s.Algorithm == "grid" ? space.Grid(n) : space.Random(s.Seed, n)) p[k] = v?.DeepClone();
        var paramsJson = p.ToJsonString();
        try
        {
            var run = await mediator.Send(new StartTrainingCommand(s.WorkspaceId, s.ProjectId, $"{s.Name}-t{n}", s.Recipe, s.Variant, s.Task,
                paramsJson, false, s.Cpus, s.Gpus, s.ImageOverride, s.Network, s.DatasetVersionId), ct);
            await db.InsertSweepTrialAsync(s.Id, n, paramsJson, run.Id, "queued", null, clock.UtcNow, ct);
        }
        catch (AppException e)
        {
            await db.InsertSweepTrialAsync(s.Id, n, paramsJson, null, "failed", $"{e.Reason}: {e.Message}", clock.UtcNow, ct);
            return false;
        }
        return true;
    }
}
