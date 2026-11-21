using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent;

/// <summary>Agent-side data channel (20/04 §3.2): a new TCP(+TLS) connection to the control plane, DataChannelHello →
/// DataChannelAccept, then pipes bytes both ways with 127.0.0.1.</summary>
public static class DataChannel
{
    private static readonly SemaphoreSlim Slots = new(64);

    public static async Task RunAsync(AgentOptions o, OpenDataChannel open, ILogger log, CancellationToken ct)
    {
        if (open.TargetCase != OpenDataChannel.TargetOneofCase.Notebook || open.Notebook.Port is <= 0 or > 65535) return;
        if (!await Slots.WaitAsync(0, ct)) { log.LogWarning("64 data channels in use; dropping session {Session}", open.SessionId); return; }
        try
        {
            using var local = new TcpClient { NoDelay = true };
            await local.ConnectAsync("127.0.0.1", open.Notebook.Port, ct);
            var parts = o.ControlPlane.Split(':');
            using var cp = new TcpClient { NoDelay = true };
            await cp.ConnectAsync(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 7443, ct);
            Stream remote = cp.GetStream();
            remote = await AgentTls.WrapAsync(remote, o, parts[0], ct);
            await Framing.WriteAsync(remote, new ConnectionHello { DataChannel = new DataChannelHello { SessionId = open.SessionId, Token = open.Token } }, ct);
            var accept = await Framing.ReadAsync(remote, DataChannelAccept.Parser, ct);
            if (accept is not { Ok: true }) { log.LogWarning("Data channel rejected: {Reason}", accept?.ErrorReason); return; }
            var lstream = local.GetStream();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var up = CopyAsync(lstream, remote, cts.Token);
            var down = CopyAsync(remote, lstream, cts.Token);
            await Task.WhenAny(up, down);
            await cts.CancelAsync();
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or InvalidDataException or System.Security.Authentication.AuthenticationException)
        {
            log.LogDebug("Data channel {Session} closed: {Msg}", open.SessionId, e.Message);
        }
        finally { Slots.Release(); }
    }

    private static async Task CopyAsync(Stream from, Stream to, CancellationToken ct)
    {
        try { await from.CopyToAsync(to, 64 * 1024, ct); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
}
