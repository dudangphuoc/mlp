using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

public sealed record GetRecipeQuery(Guid WorkspaceId, string Name) : IQuery<RecipeRow>;

public sealed class GetRecipeQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetRecipeQuery, RecipeRow>
{
    public async ValueTask<RecipeRow> Handle(GetRecipeQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.GetRecipeAsync(q.WorkspaceId, q.Name, ct) ?? throw new NotFoundException("CATALOG_RECIPE_NOT_FOUND", "Recipe not found");
    }
}
