using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>A user requests an upgrade of one machine: checks permission, records the job (one running job per machine). Returns the job id, or null if a job is already in progress.</summary>
public sealed record RequestAgentUpgradeCommand(Guid WorkspaceId, Guid HostId, string FromVersion, string ToVersion, bool Skip, string Message) : ICommand<Guid>;

/// <summary>Handles <see cref="RequestAgentUpgradeCommand"/>.</summary>
public sealed class RequestAgentUpgradeCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<RequestAgentUpgradeCommand, Guid>
{
    public async ValueTask<Guid> Handle(RequestAgentUpgradeCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "fleet.manage", ct);
        var id = Ids.New();
        await db.InsertAgentUpgradeAsync(id, c.WorkspaceId, c.HostId, c.FromVersion, c.ToVersion, c.Skip ? "skipped" : "queued", c.Message, uid, clock.UtcNow, ct);
        return id;
    }
}
