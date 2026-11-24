namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Where a newly opened code run lives.</summary>
public sealed record CodeRunOpened(Guid RunId, Guid WorkspaceId, string WorkspaceSlug, Guid ProjectId, string ProjectSlug, int Number);
