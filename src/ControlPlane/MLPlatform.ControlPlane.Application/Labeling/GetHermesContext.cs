using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record GetHermesContextQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<HermesContext>;

public sealed class GetHermesContextQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetHermesContextQuery, HermesContext>
{
    public async ValueTask<HermesContext> Handle(GetHermesContextQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var s = await db.GetItemStateAsync(p.Id, q.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (roles.Count > 0 && roles.All(r => r == "annotator") && s.AnnotatorId != uid) throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        return await LabelContextLoader.LoadAsync(db, p, q.Path, uid, ct);
    }
}
