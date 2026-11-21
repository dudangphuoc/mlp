using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Agent upgrade history of a workspace (filterable by machine).</summary>
[RequiresPermission("fleet.read")]
public sealed record ListAgentUpgradesQuery(Guid WorkspaceId, Guid? HostId) : IQuery<IReadOnlyList<AgentUpgradeRow>>;

/// <summary>Handles <see cref="ListAgentUpgradesQuery"/>.</summary>
public sealed class ListAgentUpgradesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListAgentUpgradesQuery, IReadOnlyList<AgentUpgradeRow>>
{
    public async ValueTask<IReadOnlyList<AgentUpgradeRow>> Handle(ListAgentUpgradesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await db.ListAgentUpgradesAsync(q.WorkspaceId, q.HostId, ct);
    }
}
