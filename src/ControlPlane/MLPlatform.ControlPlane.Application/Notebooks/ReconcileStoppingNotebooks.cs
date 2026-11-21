using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

/// <summary>Reaper: finishes "stopping" notebooks.</summary>
public sealed record ReconcileStoppingNotebooksCommand : ICommand<List<Guid>>, IInternalCommand;

public sealed class ReconcileStoppingNotebooksCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<ReconcileStoppingNotebooksCommand, List<Guid>>
{
    public async ValueTask<List<Guid>> Handle(ReconcileStoppingNotebooksCommand c, CancellationToken ct)
    {
        var resend = new List<Guid>();
        foreach (var s in await db.ListStoppingServersAsync(ct))
        {
            if (s.JobId is null || s.JobState is "" or "succeeded" or "failed" or "cancelled")
                await db.SetServerStateAsync(s.Id, "stopped", s.StateReason ?? "Stopped", null, null, clock.UtcNow, ct);
            else if (clock.UtcNow - s.StateAt > TimeSpan.FromMinutes(5))
            {
                await db.SetServerStateAsync(s.Id, "stopped", "The machine did not confirm the stop within 5 minutes; marked stopped and the stop was sent again", null, null, clock.UtcNow, ct);
                resend.Add(s.JobId.Value);
            }
        }
        return resend;
    }
}
