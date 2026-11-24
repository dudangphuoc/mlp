using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>SDK: the process of a code run ended with this exit code (0 succeeded, 130 interrupted,
/// otherwise failed).</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record FinishCodeRunCommand(Guid WorkspaceId, Guid RunId, int ExitCode) : ICommand<string>;

public sealed class FinishCodeRunCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<FinishCodeRunCommand, string>
{
    public async ValueTask<string> Handle(FinishCodeRunCommand c, CancellationToken ct)
    {
        var run = await CodeRunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, ct);
        var state = CodeRunRules.StateForExit(c.ExitCode);
        return await db.FinishCodeRunAsync(run.Id, run.WorkspaceId, state, c.ExitCode, clock.UtcNow, ct) > 0 ? state : run.State;
    }
}
