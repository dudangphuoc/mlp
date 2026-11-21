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

/// <summary>Takes a Catalog recipe into a project (idempotent): the project gets its own copy of the
/// manifest.</summary>
public sealed record AdoptRecipeCommand(Guid WorkspaceId, Guid ProjectId, string Recipe) : ICommand<ProjectRecipeView>;

/// <summary>Copies the manifest as it is now; a second call for the same recipe returns the existing copy.</summary>
public sealed class AdoptRecipeCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<AdoptRecipeCommand, ProjectRecipeView>
{
    public async ValueTask<ProjectRecipeView> Handle(AdoptRecipeCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var project = await db.GetProjectAsync(c.ProjectId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_PROJECT_NOT_FOUND", "Project not found");
        if (await db.GetProjectRecipeByNameAsync(project.Id, c.WorkspaceId, c.Recipe, ct) is { } existing)
            return await RecipeDrafts.ViewAsync(mediator, c.WorkspaceId, existing, ct);
        var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, c.Recipe), ct);
        if (!recipe.Tasks.Contains(project.Task))
            throw new PreconditionException("CATALOG_TASK_UNSUPPORTED", $"Recipe {recipe.Name} does not support task {project.Task}");
        if (JsonNode.Parse(recipe.Manifest)?["entrypoints"]?["train"] is not JsonObject)
            throw new PreconditionException("CATALOG_NO_TRAIN", $"Recipe {recipe.Name} has no train entrypoint");
        var id = Ids.New();
        await db.InsertProjectRecipeAsync(id, c.WorkspaceId, project.Id, recipe.Id, recipe.Name, recipe.Manifest, RecipeDrafts.Sha(recipe.Manifest), uid, clock.UtcNow, ct);
        return new ProjectRecipeView((await db.GetProjectRecipeAsync(id, c.WorkspaceId, ct))!, recipe.DisplayName, false);
    }
}
