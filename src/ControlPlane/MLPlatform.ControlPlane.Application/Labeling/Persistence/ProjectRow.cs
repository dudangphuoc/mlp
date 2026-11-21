using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record ProjectRow(Guid Id, Guid WorkspaceId, Guid DatasetId, string Name, string GuidelineMd, int SchemaVersion,
    string ReviewMode, int LockTtlMin, string State, DateTimeOffset CreatedAt, Guid? VersionId, double AutoAcceptThreshold, bool HermesEnabled);
