using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record ItemStateRow(Guid ProjectId, Guid DatasetId, string Path, string Status, Guid? AnnotatorId, Guid? ReviewerId,
    Guid? LockOwner, DateTimeOffset? LockExpiresAt, Guid? DraftBy, string? DraftAnnotations, DateTimeOffset? DraftSavedAt,
    string? SkipReason, int Revision);
