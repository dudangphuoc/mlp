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

/// <summary>Test replicas idle past their limit; the prober stops them (state + job).</summary>
[Access(AccessKind.Internal)]
public sealed record ListIdleTestDeploymentsQuery : IQuery<IReadOnlyList<DeploymentRow>>;

public sealed class ListIdleTestDeploymentsQueryHandler(IReadSession db, IClock clock) : IQueryHandler<ListIdleTestDeploymentsQuery, IReadOnlyList<DeploymentRow>>
{
    public async ValueTask<IReadOnlyList<DeploymentRow>> Handle(ListIdleTestDeploymentsQuery q, CancellationToken ct) =>
        await db.ListIdleTestDeploymentsAsync(clock.UtcNow, ct);
}
