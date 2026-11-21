using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

[Access(AccessKind.Internal)]
public sealed record GetRunIdForJobQuery(Guid JobId) : IQuery<Guid?>;

public sealed class GetRunIdForJobQueryHandler(IReadSession db) : IQueryHandler<GetRunIdForJobQuery, Guid?>
{
    public async ValueTask<Guid?> Handle(GetRunIdForJobQuery q, CancellationToken ct) => (await db.GetRunByJobAsync(q.JobId, ct))?.Id;
}
