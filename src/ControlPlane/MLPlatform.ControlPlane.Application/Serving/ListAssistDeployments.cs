using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Serving;

/// <summary>Deployments of the project's workspace (internal: called after the project was authorized; background jobs
/// too).</summary>
public sealed record ListAssistDeploymentsQuery(Guid WorkspaceId) : IQuery<List<DeploymentRow>>;

public sealed class ListAssistDeploymentsQueryHandler(IReadSession db)
    : IQueryHandler<ListAssistDeploymentsQuery, List<DeploymentRow>>
{
    public async ValueTask<List<DeploymentRow>> Handle(ListAssistDeploymentsQuery q, CancellationToken ct) =>
        await ServingSql.ListDeploymentsAsync(db, q.WorkspaceId, ct);
}
