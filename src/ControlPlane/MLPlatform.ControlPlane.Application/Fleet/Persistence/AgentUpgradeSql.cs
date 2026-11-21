using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Agent upgrade history queries.</summary>
public static partial class AgentUpgradeSql
{
    [SqlQuery("Fleet/InsertAgentUpgrade.sql")]
    public static partial Task InsertAgentUpgradeAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid hostId, string fromVersion, string toVersion,
        string state, string message, Guid? createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Fleet/UpdateAgentUpgrade.sql")]
    public static partial Task UpdateAgentUpgradeAsync(this IWriteSession db, Guid id, string state, string phase, string message, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Fleet/ListAgentUpgrades.sql")]
    public static partial Task<List<AgentUpgradeRow>> ListAgentUpgradesAsync(this IReadSession db, Guid workspaceId, Guid? hostId, CancellationToken ct);
    [SqlQuery("Fleet/ActiveAgentUpgrades.sql")]
    public static partial Task<List<ActiveAgentUpgradeRow>> ActiveAgentUpgradesAsync(this IReadSession db, CancellationToken ct);
}
