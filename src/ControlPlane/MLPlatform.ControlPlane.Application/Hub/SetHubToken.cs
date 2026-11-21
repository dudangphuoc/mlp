using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Saves the HF token (already encrypted + whoami-checked by the api). Owner/Admin.</summary>
public sealed record SetHubTokenCommand(Guid WorkspaceId, string SecretEnc, string? Account, bool Valid) : ICommand<bool>;

/// <summary>Handles <see cref="SetHubTokenCommand"/>.</summary>
public sealed class SetHubTokenCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<SetHubTokenCommand, bool>
{
    public async ValueTask<bool> Handle(SetHubTokenCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        await db.UpsertIntegrationAsync(c.WorkspaceId, "huggingface", c.SecretEnc, c.Account, c.Valid ? "ok" : "invalid", uid, clock.UtcNow, ct);
        return true;
    }
}
