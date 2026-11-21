using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record AnnotationRow(Guid Id, string Kind, int ClassId, string Geometry, string Attributes, string Source, double? Confidence);
