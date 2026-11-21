using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Deployments of the project's workspace (internal: called after the project was authorized; background jobs too).</summary>
public sealed record ListAssistDeploymentsQuery(Guid WorkspaceId) : IQuery<List<MLPlatform.ControlPlane.Application.Serving.DeploymentRow>>;

public sealed class ListAssistDeploymentsQueryHandler(IReadSession db)
    : IQueryHandler<ListAssistDeploymentsQuery, List<MLPlatform.ControlPlane.Application.Serving.DeploymentRow>>
{
    public async ValueTask<List<MLPlatform.ControlPlane.Application.Serving.DeploymentRow>> Handle(ListAssistDeploymentsQuery q, CancellationToken ct) =>
        await MLPlatform.ControlPlane.Application.Serving.ServingSql.ListDeploymentsAsync(db, q.WorkspaceId, ct);
}
