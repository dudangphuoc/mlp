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

/// <summary>Starts a trial of the caller's draft: freezes it, then trains it hidden on a small subset in two short
/// passes.</summary>
[RequiresPermission("job.submit")]
public sealed record StartTrialCommand(Guid WorkspaceId, Guid ProjectRecipeId) : ICommand<TrialView>;

/// <summary>Reuses the training path (job, volume, metrics, log, model upload) with a subset of the data and the
/// recipe's trial overrides.</summary>
public sealed class StartTrialCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator, S3Client s3)
    : ICommandHandler<StartTrialCommand, TrialView>
{
    public async ValueTask<TrialView> Handle(StartTrialCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var state = await mediator.Send(new GetDraftCommand(c.WorkspaceId, c.ProjectRecipeId), ct);
        if (!state.CanTrial) throw new PreconditionException("TRACK_DRAFT_NOT_READY", state.Reasons[0]);
        var (pr, d) = (state.Recipe.Row, state.Draft);
        var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
        if (manifest["entrypoints"]?["train"]?["trial"] is not JsonObject passes)
            throw new PreconditionException("CATALOG_NO_TRIAL", state.Recipe.OriginChanged
                ? "This project's copy of the recipe predates trial runs — take the Catalog version first"
                : $"Recipe {pr.OriginName} does not declare how to run a trial");
        var (rev, trialKey) = await RecipeDrafts.FreezeAsync(db, clock, c.WorkspaceId, uid, pr, d, ct);
        var schema = RecipeDrafts.Schema(manifest);
        var batch = RecipeDrafts.IntValue(RecipeDrafts.Parse(rev.Params), schema, "batch", 1);
        HashSet<string> files = [];
        int train = 0, val = 0;
        if (state.Dataset is { } data)
        {
            (files, train, val) = await RecipeTrials.SubsetAsync(db, s3, data.Version, data.State, batch, ct);
            if (batch > train)
                throw new PreconditionException("TRACK_BATCH_TOO_LARGE", $"Batch {batch} is larger than the {train} train images of the trial — lower the batch");
        }
        var run = await mediator.Send(new StartTrainingCommand(c.WorkspaceId, pr.ProjectId, "", pr.OriginName, rev.Variant, "", rev.Params,
            d.StartKind == "backbone", d.Cpus, d.Gpus, null, null, d.DatasetVersionId, rev.Manifest, d.HostId, new TrialSpec(files, passes)), ct);
        await db.SetRunRevisionAsync(run.Id, c.WorkspaceId, rev.Id, d.TrainKind, d.StartKind, null, d.HostId, ct);
        var id = Ids.New();
        await db.InsertTrialAsync(id, c.WorkspaceId, pr.ProjectId, pr.Id, rev.Id, run.Id, trialKey, d.DatasetVersionId, d.StartKind, d.HostId, d.Gpus, train, val,
            uid, clock.UtcNow, ct);
        return await RecipeTrials.ViewAsync(db, (await db.GetTrialAsync(id, c.WorkspaceId, ct))!, null, ct);
    }
}
