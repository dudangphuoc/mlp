using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>A peer of the WireGuard hub.</summary>
public sealed record OverlayPeerRow(string WgPublicKey, string OverlayIp);
