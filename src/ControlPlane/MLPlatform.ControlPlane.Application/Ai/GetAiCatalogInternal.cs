using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Catalog for the runtime (internal; no permission check — used while serving a request that was already authorized).</summary>
public sealed record GetAiCatalogInternalQuery : IQuery<AiOverview>;

public sealed class GetAiCatalogInternalQueryHandler(IReadSession db, IClock clock) : IQueryHandler<GetAiCatalogInternalQuery, AiOverview>
{
    public async ValueTask<AiOverview> Handle(GetAiCatalogInternalQuery q, CancellationToken ct) =>
        new(await db.ListAiConnectionsAsync(ct), await db.ListAiModelsAsync(ct), await db.ListAiRoleDefaultsAsync(ct), await db.AiModelStatsAsync(clock.UtcNow.AddHours(-24), ct));
}
