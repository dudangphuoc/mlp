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

public sealed record GetMonitorQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<MonitorView>;

public sealed class GetMonitorQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetMonitorQuery, MonitorView>
{
    public async ValueTask<MonitorView> Handle(GetMonitorQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return new MonitorView(await db.GetConfigAsync(q.DeploymentId, ct), (await db.ListSnapshotsAsync(q.DeploymentId, 1, ct)).FirstOrDefault());
    }
}
