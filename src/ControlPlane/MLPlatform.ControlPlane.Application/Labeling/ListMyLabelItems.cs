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
public sealed record ListMyLabelItemsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<MyItemRow>>;

public sealed class ListMyLabelItemsQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListMyLabelItemsQuery, IReadOnlyList<MyItemRow>>
{
    public async ValueTask<IReadOnlyList<MyItemRow>> Handle(ListMyLabelItemsQuery q, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        return await db.ListMyItemsAsync(p.Id, uid, ct);
    }
}
