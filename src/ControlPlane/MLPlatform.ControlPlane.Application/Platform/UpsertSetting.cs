using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>Saves a setting (the api has already merged + encrypted secrets).</summary>
public sealed record UpsertSettingCommand(string Key, bool Enabled, string ConfigJson, string? SecretsEnc) : ICommand<bool>;

/// <summary>Handles <see cref="UpsertSettingCommand"/>.</summary>
public sealed class UpsertSettingCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<UpsertSettingCommand, bool>
{
    public async ValueTask<bool> Handle(UpsertSettingCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        await db.UpsertSettingAsync(c.Key, c.Enabled, c.ConfigJson, c.SecretsEnc, uid, clock.UtcNow, ct);
        return true;
    }
}
