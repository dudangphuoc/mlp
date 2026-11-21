using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>One agent upgrade (D-38).</summary>
public sealed record AgentUpgradeRow(Guid Id, Guid HostId, string HostName, string FromVersion, string ToVersion, string State, string Phase,
    string Message, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);
