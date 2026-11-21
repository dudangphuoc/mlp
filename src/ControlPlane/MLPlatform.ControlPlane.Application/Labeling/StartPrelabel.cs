using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

[Access(AccessKind.ScopedPermission, "labeling.annotate")]
public sealed record StartPrelabelCommand(Guid WorkspaceId, Guid ProjectId, string SourceKind, Guid SourceId, string SourceLabel, int Limit) : ICommand<PrelabelJobRow>;

public sealed class StartPrelabelCommandHandler(IWriteSession db, LabelAccess access, WorkspaceAuthorizer auth, IClock clock) : ICommandHandler<StartPrelabelCommand, PrelabelJobRow>
{
    public async ValueTask<PrelabelJobRow> Handle(StartPrelabelCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await AssistAccess.ManageAsync(access, auth, c.WorkspaceId, c.ProjectId, ct);
        LabelAccess.RequireActive(p);
        var targets = await db.CountPrelabelTargetsAsync(p.Id, p.WorkspaceId, ct);
        if (targets == 0) throw new PreconditionException("LABEL_NOTHING_TO_PRELABEL", "Every item already has labels or a pending suggestion");
        var total = (int)Math.Min(targets, c.Limit <= 0 ? targets : c.Limit);
        var id = Ids.New();
        await db.InsertPrelabelJobAsync(id, p.WorkspaceId, p.Id, c.SourceKind, c.SourceId, c.SourceLabel, total, uid, clock.UtcNow, ct);
        return (await db.GetPrelabelJobAsync(id, p.WorkspaceId, ct))!;
    }
}
