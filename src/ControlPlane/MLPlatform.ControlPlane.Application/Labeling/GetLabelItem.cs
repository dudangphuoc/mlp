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
public sealed record GetLabelItemQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<ItemView>;

public sealed class GetLabelItemQueryHandler(IReadSession db, LabelAccess access, S3Client s3) : IQueryHandler<GetLabelItemQuery, ItemView>
{
    public async ValueTask<ItemView> Handle(GetLabelItemQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var s = await db.GetItemStateAsync(p.Id, q.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        var onlyAnnotator = roles.Count > 0 && roles.All(r => r == "annotator");
        if (onlyAnnotator && s.AnnotatorId != uid) throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        return await access.ViewAsync(p, q.Path, s3, ct);
    }
}
