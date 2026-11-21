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
public sealed record ListPredictionsQuery(Guid WorkspaceId, Guid DeploymentId, int Limit) : IQuery<IReadOnlyList<PredictionRow>>;

public sealed class ListPredictionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPredictionsQuery, IReadOnlyList<PredictionRow>>
{
    public async ValueTask<IReadOnlyList<PredictionRow>> Handle(ListPredictionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.RecentPredictionsAsync(q.DeploymentId, q.Limit is > 0 and <= 500 ? q.Limit : 50, ct);
    }
}
