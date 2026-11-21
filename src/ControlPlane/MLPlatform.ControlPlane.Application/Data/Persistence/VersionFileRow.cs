using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>A file of a version.</summary>
public sealed record VersionFileRow(string Path, long SizeBytes, string? Url);
