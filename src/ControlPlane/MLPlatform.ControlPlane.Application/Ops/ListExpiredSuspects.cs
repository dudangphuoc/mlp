using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

public sealed record ListExpiredSuspectsQuery : IQuery<IReadOnlyList<SuspectRow>>;

public sealed class ListExpiredSuspectsQueryHandler(IReadSession db, IClock clock) : IQueryHandler<ListExpiredSuspectsQuery, IReadOnlyList<SuspectRow>>
{
    public async ValueTask<IReadOnlyList<SuspectRow>> Handle(ListExpiredSuspectsQuery q, CancellationToken ct) => await db.ExpiredSuspectsAsync(clock.UtcNow, ct);
}
