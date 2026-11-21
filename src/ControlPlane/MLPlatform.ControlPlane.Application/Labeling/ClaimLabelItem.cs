using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Open one chosen item for editing — from the item list or the dataset preview — instead of taking the next
/// one in the queue.</summary>
public sealed record ClaimLabelItemCommand(Guid WorkspaceId, Guid ProjectId, string Path) : ICommand<ItemView>;

/// <summary>Locks the item for the caller; refuses when someone else holds it.</summary>
public sealed class ClaimLabelItemCommandHandler(IWriteSession db, LabelAccess access, IClock clock, S3Client s3) : ICommandHandler<ClaimLabelItemCommand, ItemView>
{
    public async ValueTask<ItemView> Handle(ClaimLabelItemCommand c, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        LabelAccess.RequireActive(p);
        if (!roles.Contains("annotator") && !roles.Contains("lead")) throw new ForbiddenException("LABEL_NOT_MEMBER", "You are not an Annotator of this project");
        var now = clock.UtcNow;
        _ = await db.GetItemStateAsync(p.Id, c.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (await db.ClaimItemAsync(p.Id, c.Path, uid, now, now.AddMinutes(p.LockTtlMin), ct) is null)
            throw new PreconditionException("LABEL_ITEM_LOCKED", "Item is being labeled by someone else");
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, c.Path, "open", null, null, uid, now, ct);
        return await access.ViewAsync(p, c.Path, s3, ct, db);
    }
}
