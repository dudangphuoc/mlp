using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Reads used by comparison and diff: the recipe copy's parameter schema, a value as people see it.</summary>
public static partial class ComparisonSql
{
    [SqlQuery("Track/RecipeUsage.sql")]
    public static partial Task<List<RecipeUsageRow>> RecipeUsageAsync(this IReadSession db, Guid workspaceId, string recipe, CancellationToken ct);
}
