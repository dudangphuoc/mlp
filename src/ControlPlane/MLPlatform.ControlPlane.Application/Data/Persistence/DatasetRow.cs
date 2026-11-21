using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

public sealed record DatasetRow(Guid Id, Guid WorkspaceId, string Slug, string Name, string Task, string Status, DateTimeOffset CreatedAt,
    long FileCount, long SizeBytes, int CurrentVersion, long PendingFiles = 0, long PendingRemovals = 0);
