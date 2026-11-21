using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

public sealed record DeleteAiConnectionCommand(Guid Id) : ICommand<bool>;

public sealed class DeleteAiConnectionCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<DeleteAiConnectionCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteAiConnectionCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var old = await db.GetAiConnectionAsync(c.Id, ct) ?? throw new NotFoundException("AI_CONNECTION_NOT_FOUND", "Connection not found");
        await db.DeleteAiConnectionAsync(c.Id, ct);
        await AiAudit.WriteAsync(db, clock, uid, "connection.delete", old.Name, AiAudit.Of(old), null, ct);
        return true;
    }
}
