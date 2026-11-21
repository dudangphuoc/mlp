using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>An immutable snapshot of a draft's recipe content: manifest, variant and the parameter values that were set.</summary>
public sealed record RevisionRow(Guid Id, Guid ProjectRecipeId, int Number, Guid? ParentRevisionId, string Manifest, string ManifestSha256, string Variant,
    string Params, string ContentSha256, Guid CreatedBy, DateTimeOffset CreatedAt);
