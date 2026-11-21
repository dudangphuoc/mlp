using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Security;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Peer list for the WireGuard hub (P0-16): every active machine that has an mlp-net key.</summary>
public sealed record ListOverlayPeersQuery : IQuery<IReadOnlyList<OverlayPeerRow>>;

/// <summary>Handles <see cref="ListOverlayPeersQuery"/> (internal background loop, no user authorization).</summary>
public sealed class ListOverlayPeersQueryHandler(IReadSession db) : IQueryHandler<ListOverlayPeersQuery, IReadOnlyList<OverlayPeerRow>>
{
    public async ValueTask<IReadOnlyList<OverlayPeerRow>> Handle(ListOverlayPeersQuery q, CancellationToken ct) => await db.ListOverlayPeersAsync(ct);
}

/// <summary>Stores the machine's mlp-net public key (the control plane obtains it via ContainerRunOnce keygen on the machine — D-38).</summary>
public sealed record SetHostWgKeyCommand(Guid HostId, string WgPublicKey) : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="SetHostWgKeyCommand"/>.</summary>
public sealed class SetHostWgKeyCommandHandler(IWriteSession db) : ICommandHandler<SetHostWgKeyCommand, bool>
{
    public async ValueTask<bool> Handle(SetHostWgKeyCommand c, CancellationToken ct)
    {
        await db.SetHostWgKeyAsync(c.HostId, c.WgPublicKey, ct);
        return true;
    }
}

/// <summary>VPN status of a machine.</summary>
public sealed record HostVpnRow(Guid Id, string VpnStatus, string? VpnMessage, DateTimeOffset? VpnCheckedAt);

/// <summary>VPN status queries.</summary>
public static partial class HostVpnSql
{
    [SqlQuery("Fleet/SetHostVpn.sql")]
    public static partial Task SetHostVpnAsync(this IWriteSession db, Guid id, string status, string? message, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Fleet/ListHostVpn.sql")]
    public static partial Task<List<HostVpnRow>> ListHostVpnAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
}

/// <summary>Records the tunnel check result (internal, from the agent session).</summary>
public sealed record SetHostVpnCommand(Guid HostId, string Status, string? Message) : ICommand<bool>, IInternalCommand;
/// <summary>Handles <see cref="SetHostVpnCommand"/>.</summary>
public sealed class SetHostVpnCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetHostVpnCommand, bool>
{
    public async ValueTask<bool> Handle(SetHostVpnCommand c, CancellationToken ct)
    {
        await db.SetHostVpnAsync(c.HostId, c.Status, c.Message, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>VPN status of the workspace's machines.</summary>
public sealed record ListHostVpnQuery(Guid WorkspaceId) : IQuery<List<HostVpnRow>>;
/// <summary>Handles <see cref="ListHostVpnQuery"/>.</summary>
public sealed class ListHostVpnQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListHostVpnQuery, List<HostVpnRow>>
{
    public async ValueTask<List<HostVpnRow>> Handle(ListHostVpnQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await db.ListHostVpnAsync(q.WorkspaceId, ct);
    }
}

/// <summary>Machine Docker health queries.</summary>
public static partial class HostDockerSql
{
    [SqlQuery("Fleet/SetHostDocker.sql")]
    public static partial Task<bool?> SetHostDockerAsync(this IWriteSession db, Guid id, bool ok, string error, CancellationToken ct);
}

/// <summary>Agent reports whether Docker is running on the machine (every 15 s). State change → records a machine event.</summary>
public sealed record SetHostDockerCommand(Guid HostId, bool Ok, string Error) : ICommand<bool>, IInternalCommand;
/// <summary>Handles <see cref="SetHostDockerCommand"/>.</summary>
public sealed class SetHostDockerCommandHandler(IWriteSession db) : ICommandHandler<SetHostDockerCommand, bool>
{
    public async ValueTask<bool> Handle(SetHostDockerCommand c, CancellationToken ct) => await db.SetHostDockerAsync(c.HostId, c.Ok, c.Error, ct) ?? false;
}
