using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

/// <summary>Opening a root shell on a machine: owner only, with a typed reason (docs/agent-upgrade.md §7.4).</summary>
[RequiresPermission("fleet.terminal")]
public sealed record OpenTerminalSessionCommand(Guid WorkspaceId, Guid HostId, Guid SessionId, string Reason, string Shell) : ICommand<Guid>;

public sealed class OpenTerminalSessionCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, AuditDetails details)
    : ICommandHandler<OpenTerminalSessionCommand, Guid>
{
    public async ValueTask<Guid> Handle(OpenTerminalSessionCommand r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.terminal", ct);
        var reason = r.Reason.Trim();
        details.Set("session", r.SessionId.ToString());
        details.Set("host", r.HostId.ToString());
        details.Set("shell", r.Shell);
        details.Set("reason", reason.Length > 500 ? reason[..500] : reason);
        if (reason.Length < 3) throw new ValidationException(new Dictionary<string, string> { ["reason"] = "Required" });
        return r.SessionId;
    }
}
