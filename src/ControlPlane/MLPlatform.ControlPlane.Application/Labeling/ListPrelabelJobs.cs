using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record ListPrelabelJobsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<(List<PrelabelJobRow> Jobs, long Targets)>;

public sealed class ListPrelabelJobsQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListPrelabelJobsQuery, (List<PrelabelJobRow>, long)>
{
    public async ValueTask<(List<PrelabelJobRow>, long)> Handle(ListPrelabelJobsQuery q, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        return (await db.ListPrelabelJobsAsync(p.WorkspaceId, p.Id, ct), await db.CountPrelabelTargetsAsync(p.Id, p.WorkspaceId, ct));
    }
}
