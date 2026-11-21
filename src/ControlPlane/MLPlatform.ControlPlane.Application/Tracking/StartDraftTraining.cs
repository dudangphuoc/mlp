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

/// <summary>Trains the caller's draft (canvas path): freezes it into a revision, starts the run, and records what the
/// run cooked.</summary>
public sealed record StartDraftTrainingCommand(Guid WorkspaceId, Guid ProjectRecipeId) : ICommand<RunRow>;

/// <summary>The same content never makes a second revision: an unchanged draft trains its existing revision
/// again.</summary>
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
        var host = d.HostId ?? trial.HostId;
        var run = await mediator.Send(new StartTrainingCommand(c.WorkspaceId, pr.ProjectId, d.RunName, pr.OriginName, rev.Variant, "", rev.Params,
            d.StartKind == "backbone", d.Cpus, d.Gpus, null, null, d.DatasetVersionId, rev.Manifest, host,
            GpuMemoryMb: trial.PeakVramMb is > 0 ? (int)(trial.PeakVramMb.Value * 1.25) + 512 : 0), ct);
        await db.SetRunRevisionAsync(run.Id, c.WorkspaceId, rev.Id, d.TrainKind, d.StartKind, d.TrainKind == "retrain" ? d.RetrainOfRunId : null, host, ct);
        await db.SetRunTrialAsync(run.Id, c.WorkspaceId, trial.Id, ct);
        await db.UpdateDraftAsync(d.Id, c.WorkspaceId, rev.Id, d.Variant, d.Params, d.DatasetVersionId, d.StartKind, "fine_tune", null, d.HostId, d.Gpus, d.Cpus, "",
            -1, clock.UtcNow, ct);
        return run;
    }
}
