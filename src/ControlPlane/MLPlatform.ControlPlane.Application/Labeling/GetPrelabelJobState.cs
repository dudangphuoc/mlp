using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record GetPrelabelJobStateQuery(Guid Id, Guid WorkspaceId) : IQuery<string?>;

public sealed class GetPrelabelJobStateQueryHandler(IReadSession db) : IQueryHandler<GetPrelabelJobStateQuery, string?>
{
    public async ValueTask<string?> Handle(GetPrelabelJobStateQuery q, CancellationToken ct) => (await db.GetPrelabelJobAsync(q.Id, q.WorkspaceId, ct))?.State;
}
