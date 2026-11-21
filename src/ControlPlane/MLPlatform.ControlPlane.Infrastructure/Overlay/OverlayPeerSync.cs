using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Write the peer list for the hub every 5 s (only on change; the hub compares contents).</summary>
public sealed class OverlayPeerSync(IServiceScopeFactory scopes, IConfiguration cfg, SystemHealth health, ILogger<OverlayPeerSync> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (cfg["WireGuard:PeersFile"] is not { Length: > 0 } file) return;
        var last = "";
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            health.Beat("overlay", 5);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var peers = await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new ListOverlayPeersQuery(), stop);
                var text = string.Concat(peers.Select(p => $"{p.WgPublicKey} {p.OverlayIp}\n"));
                if (text != last)
                {
                    await File.WriteAllTextAsync(file + ".tmp", text, stop);
                    File.Move(file + ".tmp", file, overwrite: true);
                    log.LogInformation("Overlay: {N} peer", peers.Count);
                    last = text;
                }
            }
            catch (Exception e) when (e is IOException or Npgsql.NpgsqlException) { log.LogWarning("Overlay peer sync failed: {Msg}", e.Message); }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
