using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Internal (scheduler): online machines of the workspace, no user permission check.</summary>
public sealed record ListOnlineHostsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<HostRow>>;

public sealed class ListOnlineHostsQueryHandler(IReadSession db) : IQueryHandler<ListOnlineHostsQuery, IReadOnlyList<HostRow>>
{
    public async ValueTask<IReadOnlyList<HostRow>> Handle(ListOnlineHostsQuery q, CancellationToken ct) => await db.ListOnlineHostsAsync(q.WorkspaceId, ct);
}
