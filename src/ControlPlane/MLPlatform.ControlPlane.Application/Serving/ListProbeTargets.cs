using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Serving;

public sealed record ListProbeTargetsQuery : IQuery<IReadOnlyList<DeploymentRow>>;

public sealed class ListProbeTargetsQueryHandler(IReadSession db) : IQueryHandler<ListProbeTargetsQuery, IReadOnlyList<DeploymentRow>>
{
    public async ValueTask<IReadOnlyList<DeploymentRow>> Handle(ListProbeTargetsQuery q, CancellationToken ct) => await db.ListDeployingWithEndpointAsync(ct);
}
