using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

[Access(AccessKind.ScopedPermission, "labeling.annotate")]
public sealed record GetLabelHistoryQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<IReadOnlyList<HistoryRow>>;

public sealed class GetLabelHistoryQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetLabelHistoryQuery, IReadOnlyList<HistoryRow>>
{
    public async ValueTask<IReadOnlyList<HistoryRow>> Handle(GetLabelHistoryQuery q, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        if (!roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "Only a Lead/Data Owner can view history");
        return await db.ListHistoryAsync(p.DatasetId, q.Path, ct);
    }
}
