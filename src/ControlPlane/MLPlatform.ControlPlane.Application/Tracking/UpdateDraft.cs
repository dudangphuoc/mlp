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

/// <summary>Saves the caller's draft.</summary>
[RequiresPermission("job.submit")]
public sealed record UpdateDraftCommand(Guid WorkspaceId, DraftInput Draft, int Etag) : ICommand<DraftView>;

/// <summary>Checks the shape of the values (unknown parameters, kinds, limits); whether they can train is the gate's
/// job, not a save error.</summary>
public sealed class UpdateDraftCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<UpdateDraftCommand, DraftView>
{
    public async ValueTask<DraftView> Handle(UpdateDraftCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var i = c.Draft;
        var pr = await db.GetProjectRecipeAsync(i.ProjectRecipeId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_RECIPE_NOT_FOUND", "Project recipe not found");
        var project = (await db.GetProjectAsync(pr.ProjectId, c.WorkspaceId, ct))!;
        var draft = await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_DRAFT_NOT_FOUND", "Open the recipe first");
        var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
        var schema = RecipeDrafts.Schema(manifest);
        var values = RecipeDrafts.Parse(i.ParamsJson);
        var unknown = values.Select(p => p.Key).Where(k => !RecipeDrafts.Settable(schema, k)).ToList();
        if (unknown.Count > 0)
            throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Parameters not in the recipe: " + string.Join(", ", unknown) });
        if (i.StartKind is not ("pretrained" or "backbone")) throw new InvalidArgumentException("TRACK_DRAFT_INVALID", "start_kind is pretrained or backbone");
        if (i.StartKind == "backbone" && manifest["entrypoints"]?["train"]?["fromScratch"]?.GetValue<bool>() != true)
            throw new PreconditionException("CATALOG_FROM_SCRATCH_UNSUPPORTED", "Recipe does not support training from scratch (20/08 §4.2)");
        if (i.TrainKind is not ("fine_tune" or "retrain")) throw new InvalidArgumentException("TRACK_DRAFT_INVALID", "train_kind is fine_tune or retrain");
        Guid? retrainOf = null;
        if (i.TrainKind == "retrain")
        {
            if (draft.TrainKind != "retrain" || draft.RetrainOfRunId is null || draft.BaseRevisionId is null)
                throw new PreconditionException("TRACK_RETRAIN_NOT_STARTED", "Open \"Train again\" from a finished run first");
            var locked = (await db.GetRevisionAsync(draft.BaseRevisionId.Value, c.WorkspaceId, ct))!;
            if (i.Variant != locked.Variant || RecipeDrafts.Normalise(values, schema).ToJsonString() != RecipeDrafts.Normalise(RecipeDrafts.Parse(locked.Params), schema).ToJsonString()
                || i.StartKind != draft.StartKind)
                throw new PreconditionException("TRACK_RETRAIN_LOCKED", "Train again keeps the recipe of the run — switch to Fine-tune to change it");
            retrainOf = draft.RetrainOfRunId;
        }
        if (i.Gpus is < 0 or > 16 || i.Cpus is <= 0 or > 512) throw new InvalidArgumentException("TRACK_RESOURCES_INVALID", "GPUs must be between 0 and 16, CPUs above 0");
        if (i.RunName.Trim().Length > 80) throw new InvalidArgumentException("TRACK_NAME_INVALID", "Run name must be at most 80 characters");
        if (i.DatasetVersionId is { } dv && await db.GetVersionAsync(dv, c.WorkspaceId, ct) is null)
            throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var saved = await db.UpdateDraftAsync(draft.Id, c.WorkspaceId, draft.BaseRevisionId, i.Variant, RecipeDrafts.Normalise(values, schema).ToJsonString(), i.DatasetVersionId,
            i.StartKind, i.TrainKind, retrainOf, i.HostId, i.Gpus, i.Cpus, i.RunName.Trim(), c.Etag, clock.UtcNow, ct);
        if (saved == 0) throw new AbortedException("TRACK_DRAFT_CONFLICT", "The draft was changed in another tab — reload");
        return await RecipeDrafts.StateAsync(db, mediator, c.WorkspaceId, pr, (await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct))!, project.Task, ct);
    }
}
