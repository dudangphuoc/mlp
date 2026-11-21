using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

[Access(AccessKind.Internal)]
public sealed record ListActiveAttemptsQuery : IQuery<IReadOnlyList<AttemptRow>>;

public sealed class ListActiveAttemptsQueryHandler(IReadSession db) : IQueryHandler<ListActiveAttemptsQuery, IReadOnlyList<AttemptRow>>
{
    public async ValueTask<IReadOnlyList<AttemptRow>> Handle(ListActiveAttemptsQuery q, CancellationToken ct) => await db.ActiveAttemptsAsync(ct);
}
