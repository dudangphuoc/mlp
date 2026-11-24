using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Stop fetching code from this URL prefix; runs already started keep their snapshot.</summary>
[Access(AccessKind.ScopedPermission, "source.manage")]
public sealed record DeleteCodeSourceCommand(Guid WorkspaceId, Guid Id) : ICommand<bool>;

public sealed class DeleteCodeSourceCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteCodeSourceCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteCodeSourceCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "source.manage", ct);
        return await db.DeleteCodeSourceAsync(c.Id, c.WorkspaceId, ct) > 0;
    }
}
