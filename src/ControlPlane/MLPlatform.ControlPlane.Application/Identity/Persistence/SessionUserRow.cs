namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Who a bearer token or cookie belongs to; an API key may be limited to one workspace.</summary>
public sealed record SessionUserRow(Guid UserId, Guid? WorkspaceId);
