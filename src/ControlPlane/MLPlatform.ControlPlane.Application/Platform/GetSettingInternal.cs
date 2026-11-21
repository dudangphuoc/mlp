using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>Reads one setting for internal services (email sending, Telegram) — not exposed over RPC.</summary>
[Access(AccessKind.Internal)]
public sealed record GetSettingInternalQuery(string Key) : IQuery<SettingRow?>;

/// <summary>Handles <see cref="GetSettingInternalQuery"/>.</summary>
public sealed class GetSettingInternalQueryHandler(IReadSession db) : IQueryHandler<GetSettingInternalQuery, SettingRow?>
{
    public async ValueTask<SettingRow?> Handle(GetSettingInternalQuery q, CancellationToken ct) => await db.GetSettingAsync(q.Key, ct);
}
