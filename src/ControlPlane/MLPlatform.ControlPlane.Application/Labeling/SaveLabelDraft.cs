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

/// <summary>R1: draft auto-save — kept when the lock expires so the next person can choose "Use draft".</summary>
public sealed record SaveLabelDraftCommand(Guid WorkspaceId, Guid ProjectId, string Path, IReadOnlyList<AnnotationInput> Annotations) : ICommand<bool>;

public sealed class SaveLabelDraftCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<SaveLabelDraftCommand, bool>
{
    public async ValueTask<bool> Handle(SaveLabelDraftCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        var s = await db.LockItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        access.RequireLock(s, uid);
        await db.SaveDraftAsync(p.Id, c.Path, LabelRules.ToJson(c.Annotations.Select(a => (a.Kind, a.ClassId, a.Geometry, a.Attributes, a.Source))), uid, clock.UtcNow, ct);
        return true;
    }
}
