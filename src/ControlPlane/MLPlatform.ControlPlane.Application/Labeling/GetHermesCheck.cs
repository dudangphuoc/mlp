using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record GetHermesCheckQuery(Guid WorkspaceId, Guid ProjectId, Guid CheckId) : IQuery<HermesCheckRow>;

public sealed class GetHermesCheckQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetHermesCheckQuery, HermesCheckRow>
{
    public async ValueTask<HermesCheckRow> Handle(GetHermesCheckQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var c = await db.GetHermesCheckAsync(q.CheckId, p.WorkspaceId, ct);
        if (c is null || c.ProjectId != p.Id) throw new NotFoundException("HERMES_CHECK_NOT_FOUND", "Check not found");
        if (roles.Count > 0 && roles.All(r => r == "annotator") && (await db.GetItemStateAsync(p.Id, c.Path, ct))?.AnnotatorId != uid)
            throw new NotFoundException("HERMES_CHECK_NOT_FOUND", "Check not found");
        return c;
    }
}
