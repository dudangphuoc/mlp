using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

public sealed record ListRecipesQuery(Guid WorkspaceId, string Task) : IQuery<IReadOnlyList<RecipeRow>>;

public sealed class ListRecipesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListRecipesQuery, IReadOnlyList<RecipeRow>>
{
    public async ValueTask<IReadOnlyList<RecipeRow>> Handle(ListRecipesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);   // every member can view the catalog
        return await db.ListRecipesAsync(q.WorkspaceId, q.Task, ct);
    }
}
