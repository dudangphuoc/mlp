using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record LabelSetSourceRow(string Path, string Kind, int ClassId, string Geometry, string Attributes, string Source);
