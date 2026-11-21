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

/// <summary>The recipes a project works with.</summary>
[RequiresPermission("run.read")]
public sealed record ListProjectRecipesQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<ProjectRecipeView>>;

/// <summary>Lists the project's copies and compares each with the Catalog recipe it came from.</summary>
public sealed class ListProjectRecipesQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IMediator mediator) : IQueryHandler<ListProjectRecipesQuery, IReadOnlyList<ProjectRecipeView>>
{
    public async ValueTask<IReadOnlyList<ProjectRecipeView>> Handle(ListProjectRecipesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var res = new List<ProjectRecipeView>();
        foreach (var pr in await db.ListProjectRecipesAsync(q.ProjectId, q.WorkspaceId, ct)) res.Add(await RecipeDrafts.ViewAsync(mediator, q.WorkspaceId, pr, ct));
        return res;
    }
}
