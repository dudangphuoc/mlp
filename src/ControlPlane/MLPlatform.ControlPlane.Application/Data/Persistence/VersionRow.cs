using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

public sealed record VersionRow(Guid Id, Guid DatasetId, int Number, string Prefix, long FileCount, long SizeBytes, string Note, DateTimeOffset CreatedAt,
    string Storage, string? SourceRef);
