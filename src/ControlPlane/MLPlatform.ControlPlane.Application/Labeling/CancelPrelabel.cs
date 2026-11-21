using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record CancelPrelabelCommand(Guid WorkspaceId, Guid ProjectId, Guid JobId) : ICommand<PrelabelJobRow>;

public sealed class CancelPrelabelCommandHandler(IWriteSession db, LabelAccess access, WorkspaceAuthorizer auth, IClock clock) : ICommandHandler<CancelPrelabelCommand, PrelabelJobRow>
{
    public async ValueTask<PrelabelJobRow> Handle(CancelPrelabelCommand c, CancellationToken ct)
    {
        var (_, p, _) = await AssistAccess.ManageAsync(access, auth, c.WorkspaceId, c.ProjectId, ct);
        var j = await db.GetPrelabelJobAsync(c.JobId, p.WorkspaceId, ct);
        if (j is null || j.ProjectId != p.Id) throw new NotFoundException("LABEL_JOB_NOT_FOUND", "Pre-label job not found");
        await db.CancelPrelabelJobAsync(j.Id, p.WorkspaceId, clock.UtcNow, ct);
        return (await db.GetPrelabelJobAsync(j.Id, p.WorkspaceId, ct))!;
    }
}
