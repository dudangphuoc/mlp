using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>All dynamic settings (Instance Admin).</summary>
[Access(AccessKind.InstanceAdmin)]
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
