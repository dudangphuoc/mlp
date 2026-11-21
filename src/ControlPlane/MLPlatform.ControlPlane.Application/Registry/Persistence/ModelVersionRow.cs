using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Registry;

public sealed record ModelVersionRow(Guid Id, Guid ModelId, string ModelName, int Version, Guid? RunId, string Recipe, string Variant, string Task,
    string Stage, long FileCount, long SizeBytes, string Metrics, DateTimeOffset CreatedAt, string Source, string? SourceRef, string Storage,
    Guid? ParentVersionId = null, string? Optimization = null);
