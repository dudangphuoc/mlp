using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Monitoring;

[RequiresPermission("run.read")]
public sealed record GetQualityQuery(Guid WorkspaceId, Guid DeploymentId, int Days) : IQuery<IReadOnlyList<DailyQualityRow>>;

public sealed class GetQualityQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<GetQualityQuery, IReadOnlyList<DailyQualityRow>>
{
    public async ValueTask<IReadOnlyList<DailyQualityRow>> Handle(GetQualityQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.DailyQualityAsync(q.DeploymentId, clock.UtcNow.AddDays(-(q.Days is > 0 and <= 365 ? q.Days : 30)), ct);
    }
}
