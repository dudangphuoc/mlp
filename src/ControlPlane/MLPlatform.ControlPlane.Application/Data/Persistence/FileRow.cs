using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

public sealed record FileRow(string Path, long SizeBytes, DateTimeOffset UploadedAt);
