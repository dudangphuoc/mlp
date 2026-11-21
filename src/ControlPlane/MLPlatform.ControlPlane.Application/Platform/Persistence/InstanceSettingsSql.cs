using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

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
