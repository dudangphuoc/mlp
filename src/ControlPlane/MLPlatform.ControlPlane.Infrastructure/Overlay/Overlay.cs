using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>
/// WireGuard overlay (P0-16, D-26). The hub is the <c>wireguard</c> container (image mlp-net); the api and ray-head share its network namespace.
/// The hub writes its public key to <c>WireGuard:HubKeyFile</c> (/net/hub.pub); the api writes the peer list to <c>WireGuard:PeersFile</c> (/net/peers),
/// which the hub syncs every 2 s. No hub configured → overlay off, everything runs as before (dev runs the api on the host).
/// </summary>
public static class Overlay
{
    /// <summary>Docker network for Ray nodes/workloads on hosts with the overlay.</summary>
    public const string ContainerNetwork = "container:mlp-net";

    public static string? HubPublicKey(IConfiguration cfg)
    {
        if (cfg["WireGuard:HubKeyFile"] is { Length: > 0 } f && File.Exists(f) && File.ReadAllText(f).Trim() is { Length: > 0 } k) return k;
        return cfg["WireGuard:HubPublicKey"] is { Length: > 0 } c ? c : null;
    }
}
