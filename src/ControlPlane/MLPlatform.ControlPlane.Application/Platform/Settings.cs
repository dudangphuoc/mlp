using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>One dynamic setting (config = JSON of plain fields; secrets_enc = JSON of secret fields encrypted by the api).</summary>
public sealed record SettingRow(string Key, bool Enabled, string ConfigJson, string? SecretsEnc, Guid? UpdatedBy, DateTimeOffset UpdatedAt);

/// <summary>Queries for the platform.settings table.</summary>
public static partial class InstanceSettingsSql
{
    [SqlQuery("Platform/ListSettings.sql")]
    public static partial Task<List<SettingRow>> ListSettingsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Platform/GetSetting.sql")]
    public static partial Task<SettingRow?> GetSettingAsync(this IReadSession db, string key, CancellationToken ct);
    [SqlQuery("Platform/UpsertSetting.sql")]
    public static partial Task UpsertSettingAsync(this IWriteSession db, string key, bool enabled, string configJson, string? secretsEnc, Guid updatedBy, DateTimeOffset now, CancellationToken ct);
}

/// <summary>Only an Instance Admin may view/edit instance settings.</summary>
public sealed class InstanceAdminGuard(IReadSession db, ICurrentUser user)
{
    public async Task<Guid> RequireAsync(CancellationToken ct)
    {
        var uid = user.RequireUser();
        if (await db.GetUserByIdAsync(uid, ct) is not { IsInstanceAdmin: true })
            throw new ForbiddenException("IAM_PERMISSION_DENIED", "Only an Instance Admin can manage system settings");
        return uid;
    }
}

/// <summary>All dynamic settings (Instance Admin).</summary>
public sealed record ListSettingsQuery : IQuery<List<SettingRow>>;
/// <summary>Handles <see cref="ListSettingsQuery"/>.</summary>
public sealed class ListSettingsQueryHandler(IReadSession db, InstanceAdminGuard guard) : IQueryHandler<ListSettingsQuery, List<SettingRow>>
{
    public async ValueTask<List<SettingRow>> Handle(ListSettingsQuery q, CancellationToken ct)
    {
        await guard.RequireAsync(ct);
        return await db.ListSettingsAsync(ct);
    }
}

/// <summary>Reads one setting for internal services (email sending, Telegram) — not exposed over RPC.</summary>
public sealed record GetSettingInternalQuery(string Key) : IQuery<SettingRow?>;
/// <summary>Handles <see cref="GetSettingInternalQuery"/>.</summary>
public sealed class GetSettingInternalQueryHandler(IReadSession db) : IQueryHandler<GetSettingInternalQuery, SettingRow?>
{
    public async ValueTask<SettingRow?> Handle(GetSettingInternalQuery q, CancellationToken ct) => await db.GetSettingAsync(q.Key, ct);
}

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

/// <summary>Workspace slug ↔ id (public subdomain routing).</summary>
public sealed record WorkspaceSlugRow(Guid Id, string Slug);

/// <summary>Routing queries.</summary>
public static partial class RouteSql
{
    [SqlQuery("Platform/ListWorkspaceSlugs.sql")]
    public static partial Task<List<WorkspaceSlugRow>> ListWorkspaceSlugsAsync(this IReadSession db, CancellationToken ct);
}

/// <summary>All workspaces (id, slug) — internal, not exposed over RPC.</summary>
public sealed record ListWorkspaceSlugsQuery : IQuery<List<WorkspaceSlugRow>>;
/// <summary>Handles <see cref="ListWorkspaceSlugsQuery"/>.</summary>
public sealed class ListWorkspaceSlugsQueryHandler(IReadSession db) : IQueryHandler<ListWorkspaceSlugsQuery, List<WorkspaceSlugRow>>
{
    public async ValueTask<List<WorkspaceSlugRow>> Handle(ListWorkspaceSlugsQuery q, CancellationToken ct) => await db.ListWorkspaceSlugsAsync(ct);
}
