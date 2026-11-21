using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>WireGuard overlay (P0-16, D-26).</summary>
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
