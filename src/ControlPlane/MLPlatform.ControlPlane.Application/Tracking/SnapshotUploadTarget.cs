namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Upload target for a code snapshot; <c>Exists</c> means the workspace already has it.</summary>
public sealed record SnapshotUploadTarget(Guid WorkspaceId, string WorkspaceSlug, bool Exists, string Url);
