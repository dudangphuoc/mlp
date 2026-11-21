using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Security;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Peer list for the WireGuard hub (P0-16): every active machine that has an mlp-net key.</summary>
[Access(AccessKind.Internal)]
public sealed record ListOverlayPeersQuery : IQuery<IReadOnlyList<OverlayPeerRow>>;

/// <summary>Handles <see cref="ListOverlayPeersQuery"/> (internal background loop, no user authorization).</summary>
public sealed class ListOverlayPeersQueryHandler(IReadSession db) : IQueryHandler<ListOverlayPeersQuery, IReadOnlyList<OverlayPeerRow>>
{
    public async ValueTask<IReadOnlyList<OverlayPeerRow>> Handle(ListOverlayPeersQuery q, CancellationToken ct) => await db.ListOverlayPeersAsync(ct);
}
