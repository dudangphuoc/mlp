using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

/// <summary>Removes a stopped/failed notebook from the list (the user's home volume on the machine is kept — it is
/// shared with other notebooks).</summary>
public sealed record DeleteNotebookCommand(Guid WorkspaceId, Guid ServerId) : ICommand<bool>;

public sealed class DeleteNotebookCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteNotebookCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteNotebookCommand c, CancellationToken ct)
    {
        var s = await NotebookAccess.LoadAsync(db, auth, c.WorkspaceId, c.ServerId, ct);
        if (s.State is not ("stopped" or "failed")) throw new PreconditionException("NB_NOT_STOPPED", "Stop the notebook before deleting it");
        await db.DeleteServerAsync(s.Id, c.WorkspaceId, ct);
        return true;
    }
}
