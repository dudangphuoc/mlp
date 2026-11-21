using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A project's own copy of a Catalog recipe (D-49).</summary>
public sealed record ProjectRecipeRow(Guid Id, Guid WorkspaceId, Guid ProjectId, Guid OriginRecipeId, string OriginName, string OriginManifest,
    string OriginManifestSha256, DateTimeOffset CreatedAt, int LatestRevision);
