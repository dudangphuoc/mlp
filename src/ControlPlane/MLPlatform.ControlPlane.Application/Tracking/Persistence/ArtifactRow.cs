using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

public sealed record ArtifactRow(string Path, long SizeBytes);
