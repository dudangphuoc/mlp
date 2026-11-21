using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Set (or clear, ModelId = null) the default model of a role.</summary>
public sealed record SetAiRoleDefaultCommand(string Role, Guid? ModelId) : ICommand<bool>;

public sealed class SetAiRoleDefaultCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<SetAiRoleDefaultCommand, bool>
{
    public async ValueTask<bool> Handle(SetAiRoleDefaultCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var role = AiCatalog.Clean([c.Role], AiCatalog.Roles, "role")[0];
        var beforeId = (await db.ListAiRoleDefaultsAsync(ct)).FirstOrDefault(d => d.Role == role)?.ModelId;
        var before = beforeId is { } bid ? (await db.GetAiModelAsync(bid, ct))?.ModelId : null;
        string? after = null;
        if (c.ModelId is { } mid)
        {
            var m = await db.GetAiModelAsync(mid, ct) ?? throw new NotFoundException("AI_MODEL_NOT_FOUND", "Model not found");
            if (!m.Roles.Contains(role)) throw new PreconditionException("AI_ROLE_MISMATCH", $"Model {m.ModelId} does not serve the {role} role");
            await db.SetAiRoleDefaultAsync(role, mid, uid, clock.UtcNow, ct);
            after = m.ModelId;
        }
        else await db.ClearAiRoleDefaultAsync(role, ct);
        await AiAudit.WriteAsync(db, clock, uid, "role.default", role, before is null ? null : new JsonObject { ["model"] = before },
            after is null ? null : new JsonObject { ["model"] = after }, ct);
        return true;
    }
}
