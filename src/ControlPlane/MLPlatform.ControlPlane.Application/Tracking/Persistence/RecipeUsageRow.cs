using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A project working with a Catalog recipe ("used in" tab of the recipe page).</summary>
public sealed record RecipeUsageRow(Guid ProjectId, string ProjectName, Guid ProjectRecipeId, DateTimeOffset CreatedAt, int? LatestRevision, long RunCount,
    DateTimeOffset? LastRunAt);
