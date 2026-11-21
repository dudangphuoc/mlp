using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record NextPrelabelPathsQuery(Guid ProjectId, Guid JobId, int Limit) : IQuery<List<string>>;

public sealed class NextPrelabelPathsQueryHandler(IReadSession db) : IQueryHandler<NextPrelabelPathsQuery, List<string>>
{
    public async ValueTask<List<string>> Handle(NextPrelabelPathsQuery q, CancellationToken ct) =>
        [.. (await db.NextPrelabelPathsAsync(q.ProjectId, q.JobId, q.Limit, ct)).Select(r => r.Path)];
}
