using System.Security.Cryptography;
using System.Text;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Http.Overlay;

/// <summary>Peer list ("<pubkey> <ip>" per line) for an overlay hub on another machine; Bearer
/// WireGuard:PeersToken.</summary>
public static class OverlayPeersEndpoint
{
    public static void MapOverlayPeers(this IEndpointRouteBuilder app, IConfiguration cfg) =>
        app.MapGet("/overlay/peers", async (HttpContext http, IServiceProvider sp, CancellationToken ct) =>
        {
            if (!HasPeersToken(http, cfg["WireGuard:PeersToken"])) return Results.StatusCode(401);
            await using var scope = sp.CreateAsyncScope();
            var peers = await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new ListOverlayPeersQuery(), ct);
            return Results.Text(string.Concat(peers.Select(p => $"{p.WgPublicKey} {p.OverlayIp}\n")), "text/plain");
        });

    private static bool HasPeersToken(HttpContext http, string? expected) =>
        !string.IsNullOrEmpty(expected) && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(http.Request.Headers.Authorization.ToString()), Encoding.UTF8.GetBytes("Bearer " + expected));
}
