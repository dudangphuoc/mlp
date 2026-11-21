using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Dataset caches held by machines (one host, or all when HostId is null).</summary>
public sealed record ListDataCachesQuery(Guid WorkspaceId, Guid? HostId) : IQuery<IReadOnlyList<DataCacheRow>>;

/// <summary>Lists the dataset cache volumes known from sync reports.</summary>
public sealed class ListDataCachesQueryHandler(IReadSession db, IMediator mediator, WorkspaceAuthorizer auth) : IQueryHandler<ListDataCachesQuery, IReadOnlyList<DataCacheRow>>
{
    public async ValueTask<IReadOnlyList<DataCacheRow>> Handle(ListDataCachesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await DataCacheRow.WithUseAsync(mediator, await db.ListHostDataCachesAsync(q.WorkspaceId, q.HostId, ct), ct);
    }
}
