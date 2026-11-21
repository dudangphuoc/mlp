using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

public sealed record HostRow(Guid Id, Guid WorkspaceId, string Name, string State, string? StateReason,
    string OverlayIp, string AgentVersion, byte[]? HostInfo, DateTimeOffset LastSeenAt, DateTimeOffset? DisconnectedAt);
