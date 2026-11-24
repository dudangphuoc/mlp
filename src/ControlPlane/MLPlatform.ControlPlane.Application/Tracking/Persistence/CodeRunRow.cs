namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A run opened by the SDK from the developer's own code.</summary>
public sealed record CodeRunRow(Guid Id, Guid WorkspaceId, string State);
