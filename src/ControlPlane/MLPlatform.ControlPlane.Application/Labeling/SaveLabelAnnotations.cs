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

/// <summary>R2: submits all labels of the item + etag (= revision).</summary>
[Access(AccessKind.ScopedPermission, "labeling.annotate")]
public sealed record SaveLabelAnnotationsCommand(Guid WorkspaceId, Guid ProjectId, string Path, int Etag, IReadOnlyList<AnnotationInput> Annotations) : ICommand<int>;

public sealed class SaveLabelAnnotationsCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SaveLabelAnnotationsCommand, int>
{
    public async ValueTask<int> Handle(SaveLabelAnnotationsCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        LabelAccess.RequireActive(p);
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        access.RequireLock(s, uid);
        if (s.Revision != c.Etag) throw new AbortedException("LABEL_REVISION_CONFLICT", "Labels have changed, reload");
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        LabelRules.Validate(c.Annotations, LabelRules.ParseClasses(schema!.Classes));
        await LabelStore.ReplaceAsync(db, clock, p, c.Path, uid, c.Annotations, "save", ct);
        _ = roles;
        return s.Revision + 1;
    }
}
