using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

public sealed record GetDeploymentFeaturesQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<BindingRow?>;

public sealed class GetDeploymentFeaturesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetDeploymentFeaturesQuery, BindingRow?>
{
    public async ValueTask<BindingRow?> Handle(GetDeploymentFeaturesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.GetBindingAsync(q.DeploymentId, ct);
    }
}
