using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Security;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>VPN status of a machine.</summary>
public sealed record HostVpnRow(Guid Id, string VpnStatus, string? VpnMessage, DateTimeOffset? VpnCheckedAt);
