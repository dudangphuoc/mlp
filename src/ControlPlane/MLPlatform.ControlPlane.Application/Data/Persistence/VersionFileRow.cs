using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>A file of a version. Url is set for reference versions (https://… or s3://bucket/key), null when the file is in our storage.</summary>
public sealed record VersionFileRow(string Path, long SizeBytes, string? Url);
