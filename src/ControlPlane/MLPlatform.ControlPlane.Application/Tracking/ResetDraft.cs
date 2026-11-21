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

/// <summary>Replaces the caller's draft: from a revision, from a run ("cook again from #n" — also its data, starting
/// point and machine).</summary>
[RequiresPermission("job.submit")]
public sealed record ResetDraftCommand(Guid WorkspaceId, Guid ProjectRecipeId, int RevisionNumber, Guid? RunId, bool Origin, bool Retrain = false) : ICommand<DraftView>;

/// <summary>A run without a revision (older runs, pipelines, sweeps) is rebuilt from the fields the run kept.</summary>
public sealed class ResetDraftCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<ResetDraftCommand, DraftView>
{
    public async ValueTask<DraftView> Handle(ResetDraftCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var pr = await db.GetProjectRecipeAsync(c.ProjectRecipeId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_RECIPE_NOT_FOUND", "Project recipe not found");
        var project = (await db.GetProjectAsync(pr.ProjectId, c.WorkspaceId, ct))!;
        var d = (await mediator.Send(new GetDraftCommand(c.WorkspaceId, pr.Id), ct)).Draft;
        var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
        var schema = RecipeDrafts.Schema(manifest);
        Guid? basis = null, version = d.DatasetVersionId, host = d.HostId;
        string variant, parameters, start = d.StartKind;
        int gpus = d.Gpus; double cpus = d.Cpus;
        string trainKind = "fine_tune";
        Guid? retrainOf = null;
        if (c.Retrain && c.RunId is null) throw new InvalidArgumentException("TRACK_DRAFT_INVALID", "Train again needs a run");
        if (c.RunId is { } runId)
        {
            var run = await db.GetRunAsync(runId, ct);
            if (run is null || run.WorkspaceId != c.WorkspaceId || run.ProjectId != pr.ProjectId) throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
            if (run.Recipe != pr.OriginName) throw new PreconditionException("TRACK_RECIPE_MISMATCH", $"Run #{run.Number} used recipe {run.Recipe}");
            var extra = (await db.RunExtrasAsync(c.WorkspaceId, [run.Id], ct)).FirstOrDefault();
            var rev = extra?.RevisionId is { } rid ? await db.GetRevisionAsync(rid, c.WorkspaceId, ct) : null;
            basis = rev?.Id;
            variant = rev?.Variant ?? run.Variant;
            parameters = rev?.Params ?? RecipeDrafts.Normalise(RecipeDrafts.Parse(run.Params), schema).ToJsonString();
            (version, start, host, gpus, cpus) = (run.DatasetVersionId, run.FromScratch ? "backbone" : "pretrained", extra?.HostId, run.Gpus, run.Cpus);
            if (c.Retrain)
            {
                if (run.State != "succeeded") throw new PreconditionException("TRACK_RETRAIN_NOT_FINISHED", $"Run #{run.Number} has not finished successfully");
                if (rev is null) throw new PreconditionException("TRACK_RETRAIN_NO_REVISION", $"Run #{run.Number} was trained before revisions existed — cook it again instead");
                if (rev.ManifestSha256 != pr.OriginManifestSha256)
                    throw new PreconditionException("TRACK_RETRAIN_RECIPE_CHANGED", $"The project's recipe changed since run #{run.Number} — cook it again instead");
                (trainKind, retrainOf, version, start) = ("retrain", run.Id, null, extra?.StartKind ?? start);
            }
        }
        else if (c.Origin)
        {
            var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, pr.OriginName), ct);
            await db.UpdateProjectRecipeOriginAsync(pr.Id, c.WorkspaceId, recipe.Id, recipe.Manifest, RecipeDrafts.Sha(recipe.Manifest), ct);
            pr = (await db.GetProjectRecipeAsync(pr.Id, c.WorkspaceId, ct))!;
            manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
            schema = RecipeDrafts.Schema(manifest);
            variant = RecipeDrafts.HasVariant(manifest, d.Variant) ? d.Variant : RecipeDrafts.FirstVariant(manifest);
            parameters = RecipeDrafts.Normalise(RecipeDrafts.Parse(d.Params), schema).ToJsonString();
        }
        else
        {
            RevisionRow? rev = c.RevisionNumber > 0
                ? await db.GetRevisionByNumberAsync(pr.Id, c.WorkspaceId, c.RevisionNumber, ct) ?? throw new NotFoundException("TRACK_REVISION_NOT_FOUND", "Revision not found")
                : null;
            (basis, variant, parameters) = (rev?.Id, rev?.Variant ?? RecipeDrafts.FirstVariant(manifest), rev?.Params ?? "{}");
        }
        await db.UpdateDraftAsync(d.Id, c.WorkspaceId, basis, variant, parameters, version, start, trainKind, retrainOf, host, gpus, cpus, d.RunName, -1, clock.UtcNow, ct);
        return await RecipeDrafts.StateAsync(db, mediator, c.WorkspaceId, pr, (await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct))!, project.Task, ct);
    }
}
