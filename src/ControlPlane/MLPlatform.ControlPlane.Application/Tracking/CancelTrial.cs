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

/// <summary>Stops a waiting or running trial; returns its job so the host stops the container.</summary>
public sealed record CancelTrialCommand(Guid WorkspaceId, Guid TrialId) : ICommand<(TrialView Trial, Guid? JobId)>;

/// <summary>Marks the trial and its hidden run cancelled (a later job exit does not overwrite it).</summary>
public sealed class CancelTrialCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CancelTrialCommand, (TrialView Trial, Guid? JobId)>
{
    public async ValueTask<(TrialView Trial, Guid? JobId)> Handle(CancelTrialCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var t = await db.GetTrialAsync(c.TrialId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_TRIAL_NOT_FOUND", "Trial not found");
        if (t.State is not ("queued" or "running")) throw new PreconditionException("TRACK_TRIAL_FINISHED", "This trial has already finished");
        await db.SetTrialStateAsync(t.Id, c.WorkspaceId, "cancelled", "Stopped on request", clock.UtcNow, ct);
        if (await db.GetRunAsync(t.RunId, ct) is { } run && !RunRules.Finished(run.State)) await db.SetRunStateAsync(run.Id, "cancelled", clock.UtcNow, ct);
        return (await RecipeTrials.ViewAsync(db, (await db.GetTrialAsync(t.Id, c.WorkspaceId, ct))!, null, ct), t.JobId);
    }
}
