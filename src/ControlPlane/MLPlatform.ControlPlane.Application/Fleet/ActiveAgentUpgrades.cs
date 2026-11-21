using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>In-progress upgrade jobs across the instance.</summary>
public sealed record ActiveAgentUpgradesQuery : IQuery<IReadOnlyList<ActiveAgentUpgradeRow>>;

/// <summary>Handles <see cref="ActiveAgentUpgradesQuery"/>.</summary>
public sealed class ActiveAgentUpgradesQueryHandler(IReadSession db) : IQueryHandler<ActiveAgentUpgradesQuery, IReadOnlyList<ActiveAgentUpgradeRow>>
{
    public async ValueTask<IReadOnlyList<ActiveAgentUpgradeRow>> Handle(ActiveAgentUpgradesQuery q, CancellationToken ct) => await db.ActiveAgentUpgradesAsync(ct);
}
