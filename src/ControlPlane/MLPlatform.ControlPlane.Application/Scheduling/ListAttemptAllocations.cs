using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Machines and GPUs an attempt was given (to reserve them again when the Ray head lost them).</summary>
public sealed record ListAttemptAllocationsQuery(Guid AttemptId) : IQuery<IReadOnlyList<AllocationRow>>;

/// <summary>Reads the allocation rows of the attempt.</summary>
public sealed class ListAttemptAllocationsQueryHandler(IReadSession db) : IQueryHandler<ListAttemptAllocationsQuery, IReadOnlyList<AllocationRow>>
{
    public async ValueTask<IReadOnlyList<AllocationRow>> Handle(ListAttemptAllocationsQuery q, CancellationToken ct) => await db.ListAllocationsAsync(q.AttemptId, ct);
}
