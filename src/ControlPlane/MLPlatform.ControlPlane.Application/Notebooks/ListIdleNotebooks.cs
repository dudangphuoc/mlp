using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

[Access(AccessKind.Internal)]
public sealed record ListIdleNotebooksQuery : IQuery<IReadOnlyList<ServerRow>>;

public sealed class ListIdleNotebooksQueryHandler(IReadSession db, IClock clock) : IQueryHandler<ListIdleNotebooksQuery, IReadOnlyList<ServerRow>>
{
    public async ValueTask<IReadOnlyList<ServerRow>> Handle(ListIdleNotebooksQuery q, CancellationToken ct) => await db.ListIdleServersAsync(clock.UtcNow, ct);
}
