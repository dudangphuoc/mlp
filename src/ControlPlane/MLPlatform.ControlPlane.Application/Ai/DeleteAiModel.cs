using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

public sealed record DeleteAiModelCommand(Guid Id) : ICommand<bool>;

public sealed class DeleteAiModelCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<DeleteAiModelCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteAiModelCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var old = await db.GetAiModelAsync(c.Id, ct) ?? throw new NotFoundException("AI_MODEL_NOT_FOUND", "Model not found");
        await db.DeleteAiModelAsync(c.Id, ct);
        await AiAudit.WriteAsync(db, clock, uid, "model.delete", old.ModelId, AiAudit.Of(old), null, ct);
        return true;
    }
}
