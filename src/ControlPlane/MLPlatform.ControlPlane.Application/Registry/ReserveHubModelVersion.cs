using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>M11 R6: create the model (if missing) + an hf_hub-sourced version (recipe hf-auto) — returns the model
/// directory prefix for the worker to write files.</summary>
public sealed record ReserveHubModelVersionCommand(Guid WorkspaceId, string ModelName, string Task, string SourceRef, string Source = "hf_hub") : ICommand<(Guid VersionId, string Prefix)>;

/// <summary>Handles <see cref="ReserveHubModelVersionCommand"/>.</summary>
public sealed class ReserveHubModelVersionCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<ReserveHubModelVersionCommand, (Guid VersionId, string Prefix)>
{
    public async ValueTask<(Guid VersionId, string Prefix)> Handle(ReserveHubModelVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var name = c.ModelName.Trim();
        if (name.Length is 0 or > 128) throw new ValidationException(new Dictionary<string, string> { ["model_name"] = "Model name must be 1–128 characters" });
        var now = clock.UtcNow;
        var model = await db.GetModelByNameAsync(c.WorkspaceId, name, ct);
        Guid modelId;
        if (model is null) { modelId = Ids.New(); await db.InsertModelAsync(modelId, c.WorkspaceId, name, c.Task, "Imported from " + c.SourceRef, uid, now, ct); }
        else
        {
            if (model.Task != c.Task) throw new PreconditionException("REGISTRY_TASK_MISMATCH", $"Model '{name}' has task {model.Task}");
            modelId = model.Id;
        }
        var n = await db.NextModelVersionAsync(modelId, ct);
        var prefix = $"{c.WorkspaceId}/models/{modelId}/v{n}/";
        var id = Ids.New();
        await db.InsertHubModelVersionAsync(id, modelId, n, c.Task, prefix, uid, now, c.SourceRef, c.Source, ct);
        return (id, prefix);
    }
}
