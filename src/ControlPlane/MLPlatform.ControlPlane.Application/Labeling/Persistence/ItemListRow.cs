using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>One row of the project's item list.</summary>
public sealed record ItemListRow(string Path, string Status, Guid? AnnotatorId, Guid? ReviewerId, DateTimeOffset? SubmittedAt, DateTimeOffset? ReviewedAt,
    string? SkipReason, long Labels);
