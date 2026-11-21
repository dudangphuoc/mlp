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

/// <summary>The hidden run's job finished (called next to SyncRunStateCommand): records the stages and measurements
/// from the attempt's [mlp-trial] lines, the machine it ran on, and passed / failed.</summary>
public sealed record FinishTrialCommand(Guid JobId, Guid? AttemptId, string JobState, IReadOnlyList<string> Lines, string? Headline) : ICommand<bool>;

/// <summary>Passed = the job succeeded and no stage failed.</summary>
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
        if (state != "passed")
            stages = stages.Select(s => s.State is "running" ? s with { State = state == "cancelled" ? "skipped" : "failed" } : s.State is "pending" ? s with { State = "skipped" } : s).ToList();
        Guid? host = c.AttemptId is { } a ? (await db.GetAttemptAsync(a, ct))?.HostIds.FirstOrDefault() : null;
        if (host == Guid.Empty) host = null;
        await db.FinishTrialAsync(t.Id, t.WorkspaceId, state, RecipeTrials.StagesJson(stages), m?.StepsPerS, m?.PeakVramMb, m?.VramEstimated ?? false, m?.ValSPerBatch,
            m?.TrainImages ?? t.TrainImages, m?.ValImages ?? t.ValImages, error, host, clock.UtcNow, ct);
        return true;
    }
}
