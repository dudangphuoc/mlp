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
public sealed record ListSnapshotsQuery(Guid WorkspaceId, Guid DeploymentId, int Limit) : IQuery<IReadOnlyList<SnapshotRow>>;

public sealed class ListSnapshotsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListSnapshotsQuery, IReadOnlyList<SnapshotRow>>
{
    public async ValueTask<IReadOnlyList<SnapshotRow>> Handle(ListSnapshotsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.ListSnapshotsAsync(q.DeploymentId, q.Limit is > 0 and <= 500 ? q.Limit : 50, ct);
    }
}
