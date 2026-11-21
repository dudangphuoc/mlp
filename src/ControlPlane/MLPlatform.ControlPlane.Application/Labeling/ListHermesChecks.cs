using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record ListHermesChecksQuery(Guid WorkspaceId, Guid ProjectId, string Verdict, string Path, int Limit) : IQuery<List<HermesCheckRow>>;

public sealed class ListHermesChecksQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListHermesChecksQuery, List<HermesCheckRow>>
{
    public async ValueTask<List<HermesCheckRow>> Handle(ListHermesChecksQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        Guid? only = roles.Count > 0 && roles.All(r => r == "annotator") ? uid : null;
        return await db.ListHermesChecksAsync(p.WorkspaceId, p.Id, q.Verdict ?? "", q.Path ?? "", only, Math.Clamp(q.Limit <= 0 ? 100 : q.Limit, 1, 500), ct);
    }
}
