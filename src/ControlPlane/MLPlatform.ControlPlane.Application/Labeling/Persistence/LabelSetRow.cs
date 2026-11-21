using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record LabelSetRow(Guid Id, int Number, int SchemaVersion, long ItemCount, long AnnotationCount, string IncludeStatus, DateTimeOffset CreatedAt);
