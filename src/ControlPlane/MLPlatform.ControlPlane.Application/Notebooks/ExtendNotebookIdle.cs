using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

/// <summary>"Keep 1 more hour": pushes the activity timestamp forward so it is not auto-stopped (R4).</summary>
public sealed record ExtendNotebookIdleCommand(Guid WorkspaceId, Guid ServerId) : ICommand<ServerRow>;

public sealed class ExtendNotebookIdleCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<ExtendNotebookIdleCommand, ServerRow>
{
    public async ValueTask<ServerRow> Handle(ExtendNotebookIdleCommand c, CancellationToken ct)
    {
        var s = await NotebookAccess.LoadAsync(db, auth, c.WorkspaceId, c.ServerId, ct);
        if (s.State != "running") throw new PreconditionException("NB_NOT_RUNNING", "Notebook is not running");
        await db.TouchServerAsync(s.Id, clock.UtcNow.AddMinutes(60), ct);
        return (await db.GetServerAsync(s.Id, c.WorkspaceId, ct))!;
    }
}
