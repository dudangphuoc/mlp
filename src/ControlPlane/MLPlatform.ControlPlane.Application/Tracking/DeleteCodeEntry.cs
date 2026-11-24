using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Remove a startable script; its past runs stay.</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record DeleteCodeEntryCommand(Guid WorkspaceId, Guid Id) : ICommand<bool>;

public sealed class DeleteCodeEntryCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteCodeEntryCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteCodeEntryCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        return await db.DeleteCodeEntryAsync(c.Id, c.WorkspaceId, ct) > 0;
    }
}
