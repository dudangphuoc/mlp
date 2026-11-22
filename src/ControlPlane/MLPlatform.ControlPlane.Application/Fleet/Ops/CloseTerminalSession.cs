using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.terminal")]
public sealed record CloseTerminalSessionCommand(Guid WorkspaceId, Guid HostId, Guid SessionId, int Seconds, long BytesIn, long BytesOut,
    int? ExitCode, string? Recording) : ICommand<bool>;

public sealed class CloseTerminalSessionCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, AuditDetails details)
    : ICommandHandler<CloseTerminalSessionCommand, bool>
{
    public async ValueTask<bool> Handle(CloseTerminalSessionCommand r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.terminal", ct);
        details.Set("session", r.SessionId.ToString());
        details.Set("host", r.HostId.ToString());
        details.Set("seconds", r.Seconds);
        details.Set("bytes_in", r.BytesIn);
        details.Set("bytes_out", r.BytesOut);
        details.Set("exit_code", r.ExitCode);
        details.Set("recording", r.Recording);
        return true;
    }
}
