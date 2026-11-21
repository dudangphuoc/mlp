using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Projects using a Catalog recipe.</summary>
[RequiresPermission("run.read")]
public sealed record ListRecipeUsageQuery(Guid WorkspaceId, string Recipe) : IQuery<IReadOnlyList<RecipeUsageRow>>;

/// <summary>One query over the workspace's project copies of the recipe.</summary>
public sealed class ListRecipeUsageQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListRecipeUsageQuery, IReadOnlyList<RecipeUsageRow>>
{
    public async ValueTask<IReadOnlyList<RecipeUsageRow>> Handle(ListRecipeUsageQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.RecipeUsageAsync(q.WorkspaceId, q.Recipe, ct);
    }
}
