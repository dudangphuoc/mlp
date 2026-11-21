using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Unfinished upgrade job (resumed after the api restarts).</summary>
public sealed record ActiveAgentUpgradeRow(Guid Id, Guid WorkspaceId, Guid HostId, string ToVersion, string State);
