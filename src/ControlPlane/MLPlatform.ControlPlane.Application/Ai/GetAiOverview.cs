using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

public sealed record GetAiOverviewQuery : IQuery<AiOverview>;

public sealed class GetAiOverviewQueryHandler(IReadSession db, InstanceAdminGuard guard, IClock clock) : IQueryHandler<GetAiOverviewQuery, AiOverview>
{
    public async ValueTask<AiOverview> Handle(GetAiOverviewQuery q, CancellationToken ct)
    {
        await guard.RequireAsync(ct);
        return new AiOverview(await db.ListAiConnectionsAsync(ct), await db.ListAiModelsAsync(ct), await db.ListAiRoleDefaultsAsync(ct),
            await db.AiModelStatsAsync(clock.UtcNow.AddHours(-24), ct));
    }
}
