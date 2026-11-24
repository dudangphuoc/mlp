using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>SDK: the process of a code run is still alive.</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record HeartbeatCodeRunCommand(Guid WorkspaceId, Guid RunId) : ICommand<bool>, IInternalCommand;

public sealed class HeartbeatCodeRunCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<HeartbeatCodeRunCommand, bool>
{
    public async ValueTask<bool> Handle(HeartbeatCodeRunCommand c, CancellationToken ct)
    {
        await CodeRunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, ct);
        return await db.TouchCodeRunAsync(c.RunId, c.WorkspaceId, clock.UtcNow, ct) > 0;
    }
}
