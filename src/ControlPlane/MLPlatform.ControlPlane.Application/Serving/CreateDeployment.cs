using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Serving;

/// <summary>
/// Deploy model version (M12 §5.1, R1, R8): replica = `serve` job running `mlp-serve` in the recipe's image; the model directory is downloaded via
/// presigned GET (user = container); the handler comes from the recipe's `entrypoints.serve.handler`.
/// </summary>
public sealed record CreateDeploymentCommand(Guid WorkspaceId, string Slug, Guid ModelVersionId, double Cpus, int Gpus, string? ImageOverride,
    string Purpose = "service", int? IdleStopMin = null) : ICommand<DeploymentRow>;

public sealed class CreateDeploymentCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth, IMediator mediator)
    : ICommandHandler<CreateDeploymentCommand, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(CreateDeploymentCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        if (!IdentityRules.Slug().IsMatch(c.Slug)) throw new ValidationException(new Dictionary<string, string> { ["slug"] = "Invalid slug" });
        var v = await db.GetModelVersionAsync(c.ModelVersionId, c.WorkspaceId, ct)
                ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var prefix = (await db.GetVersionPrefixAsync(v.Id, c.WorkspaceId, ct))!;
        var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, v.Recipe), ct);
        var manifest = JsonNode.Parse(recipe.Manifest)!.AsObject();
        var handler = manifest["entrypoints"]?["serve"]?["handler"]?.ToString()
                      ?? throw new PreconditionException("SERVE_NOT_SUPPORTED", $"Recipe {recipe.Name} does not declare entrypoints.serve (M12 R8)");
        // Same rule as training: builtin recipes are built on the machine that runs the replica; only an invalid manifest is refused.
        var image = !string.IsNullOrEmpty(c.ImageOverride) ? c.ImageOverride
            : recipe.State != "invalid" ? $"recipes/{recipe.Name}:current"
            : throw new PreconditionException("CATALOG_RECIPE_NOT_VERIFIED", $"Recipe {recipe.Name} is not valid ({recipe.State})");
        var modelFiles = await ModelFiles.BuildAsync(db, s3, v.Id, c.WorkspaceId, ct);
        // "By recipe": an optimized version runs on the device it was built and checked for; any other follows its recipe.
        var gpus = c.Gpus >= 0 ? c.Gpus : await db.GetVersionDeviceAsync(v.Id, c.WorkspaceId, ct) switch
        {
            "cuda" => 1,
            "cpu" => 0,
            _ => manifest["environment"]?["gpu"]?.ToString() == "required" ? 1 : 0,
        };
        var cpus = c.Cpus <= 0 ? 1 : c.Cpus;
        var spec = new JobSpecDto
        {
            Image = image, Command = ["mlp-serve"], Cpus = cpus, Gpus = gpus, TimeoutSeconds = int.MaxValue,
            Env = new Dictionary<string, string>
            {
                ["MLP_HANDLER"] = handler, ["MLP_TASK"] = v.Task, ["MLP_MODEL_FILES"] = modelFiles, ["MLP_DEPLOYMENT"] = c.Slug,
                ["MLP_MODEL_VERSION"] = v.Id.ToString(),
            },
        };
        // The name of a stopped deployment can be used again: it restarts that deployment (same id, keys and public address).
        if (await db.GetDeploymentBySlugAsync(c.WorkspaceId, c.Slug, ct) is { } old)
        {
            if (old.State is not ("stopped" or "failed")) throw new ConflictException("SERVE_SLUG_TAKEN", "Deployment name is already in use");
            var restartJob = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "serve", $"serve-{c.Slug}", spec, 70), ct);
            await db.RestartDeploymentAsync(old.Id, c.WorkspaceId, v.Id, v.ModelName, v.Version, v.Task, restartJob, c.Purpose, c.IdleStopMin,
                cpus, c.Gpus >= 0 ? c.Gpus : null, c.ImageOverride, ct);
            return (await db.GetDeploymentAsync(old.Id, c.WorkspaceId, ct))!;
        }
        var id = Ids.New();
        try
        {
            var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "serve", $"serve-{c.Slug}", spec, 70), ct);
            await db.InsertDeploymentAsync(id, c.WorkspaceId, c.Slug, v.Id, v.ModelName, v.Version, v.Task, jobId, uid, clock.UtcNow, c.Purpose, c.IdleStopMin,
                cpus, c.Gpus >= 0 ? c.Gpus : null, c.ImageOverride, ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        {
            throw new ConflictException("SERVE_SLUG_TAKEN", "Deployment name is already in use");
        }
        return (await db.GetDeploymentAsync(id, c.WorkspaceId, ct))!;
    }
}
