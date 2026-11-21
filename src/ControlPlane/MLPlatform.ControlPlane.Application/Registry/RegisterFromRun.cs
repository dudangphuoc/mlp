using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>
/// Registers a model version from a run (M11 §5.1, R5): server-side copy of the run's model directory to
/// `mlp-artifacts/{ws}/models/{model}/v{n}/` (immutable, R1); final metrics and recipe/variant are recorded on the version (R7).
/// </summary>
public sealed record RegisterFromRunCommand(Guid WorkspaceId, Guid RunId, string ModelName, string Description) : ICommand<ModelVersionRow>;

public sealed class RegisterFromRunCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth, IMediator mediator)
    : ICommandHandler<RegisterFromRunCommand, ModelVersionRow>
{
    public async ValueTask<ModelVersionRow> Handle(RegisterFromRunCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var name = c.ModelName.Trim();
        if (name.Length is 0 or > 128) throw new ValidationException(new Dictionary<string, string> { ["model_name"] = "Model name must be 1–128 characters" });
        var info = await mediator.Query(new GetRunForRegistryQuery(c.RunId), ct);
        if (info is not { } x || x.Run.WorkspaceId != c.WorkspaceId) throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        if (x.Run.State != "succeeded") throw new PreconditionException("REGISTRY_RUN_NOT_SUCCEEDED", "Only succeeded runs can be registered");
        if (x.Artifacts.Count == 0) throw new PreconditionException("REGISTRY_ARTIFACT_MISSING", "Run has no model directory");

        var now = clock.UtcNow;
        var model = await db.GetModelByNameAsync(c.WorkspaceId, name, ct);
        Guid modelId;
        if (model is null)
        {
            modelId = Ids.New();
            await db.InsertModelAsync(modelId, c.WorkspaceId, name, x.Run.Task, c.Description, uid, now, ct);
        }
        else
        {
            if (model.Task != x.Run.Task) throw new PreconditionException("REGISTRY_TASK_MISMATCH", $"Model '{name}' has task {model.Task}, but the run has task {x.Run.Task}");
            modelId = model.Id;
        }
        var n = await db.NextModelVersionAsync(modelId, ct);
        var prefix = $"{c.WorkspaceId}/models/{modelId}/v{n}/";
        var src = RunArtifacts.Prefix(c.WorkspaceId, x.Run.Id);
        foreach (var a in x.Artifacts) await s3.CopyAsync(Buckets.Artifacts, src + a.Path, Buckets.Artifacts, prefix + a.Path, ct);
        var metrics = new JsonObject();
        foreach (var m in x.Metrics) metrics[m.Key] = m.Value;
        var id = Ids.New();
        await db.InsertModelVersionAsync(id, modelId, n, x.Run.Id, x.Run.Recipe, x.Run.Variant, x.Run.Task, prefix, x.Artifacts.Count,
            x.Artifacts.Sum(a => a.SizeBytes), metrics.ToJsonString(), uid, now, ct);
        return (await db.GetModelVersionAsync(id, c.WorkspaceId, ct))!;
    }
}
