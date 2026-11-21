using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

/// <summary>R4: warns 10 minutes before a notebook is auto-stopped (M16 `notebook.idle_warning`).</summary>
[Access(AccessKind.Internal)]
public sealed record WarnIdleNotebooksCommand : ICommand<int>;

public sealed class WarnIdleNotebooksCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<WarnIdleNotebooksCommand, int>
{
    public async ValueTask<int> Handle(WarnIdleNotebooksCommand c, CancellationToken ct)
    {
        var rows = await db.ClaimIdleWarningsAsync(clock.UtcNow, ct);
        foreach (var r in rows)
            await MLPlatform.ControlPlane.Application.Notifications.Notifier.SendAsync(db, clock, r.WorkspaceId, [r.UserId], "notebook.idle_warning",
                $"Notebook \"{r.Name}\" will stop automatically in 10 minutes if unused", $"#/notebooks/{r.Id}", $"notebook/{r.Id}", "warning", ct);
        return rows.Count;
    }
}
