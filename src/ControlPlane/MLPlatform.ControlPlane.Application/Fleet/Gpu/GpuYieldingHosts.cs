using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet.Gpu;

/// <summary>Hosts that currently yield their GPUs to another agent sharing the machine.</summary>
[Access(AccessKind.Internal)]
public sealed record GpuYieldingHostsQuery(Guid WorkspaceId) : IQuery<IReadOnlySet<Guid>>;

public sealed class GpuYieldingHostsQueryHandler(IReadSession db) : IQueryHandler<GpuYieldingHostsQuery, IReadOnlySet<Guid>>
{
    public async ValueTask<IReadOnlySet<Guid>> Handle(GpuYieldingHostsQuery q, CancellationToken ct) =>
        (await db.GpuYieldingHostsAsync(q.WorkspaceId, ct)).Select(r => r.Id).ToHashSet();
}
