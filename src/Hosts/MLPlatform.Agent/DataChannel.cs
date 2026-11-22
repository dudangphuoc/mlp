using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent;

/// <summary>Agent-side data channel (20/04 §3.2): a new TCP(+TLS) connection to the control plane, DataChannelHello →
/// DataChannelAccept, then serves the target: a local port (notebook), a terminal, or a file transfer.</summary>
public static class DataChannel
{
    private static readonly SemaphoreSlim Slots = new(64);

    public static async Task RunAsync(AgentState state, OpenDataChannel open, ILogger log, CancellationToken ct)
    {
        if (!Valid(open)) return;
        if (!await Slots.WaitAsync(0, ct)) { log.LogWarning("64 data channels in use; dropping session {Session}", open.SessionId); return; }
        try
        {
            using var cp = new TcpClient { NoDelay = true };
            if (await ConnectAsync(cp, state.Options, open, log, ct) is not { } remote) return;
            await using (remote) await ServeAsync(state, open, remote, log, ct);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or InvalidDataException
            or UnauthorizedAccessException or System.Security.Authentication.AuthenticationException)
        {
            log.LogDebug("Data channel {Session} closed: {Msg}", open.SessionId, e.Message);
        }
        finally { Slots.Release(); }
    }

    private static bool Valid(OpenDataChannel open) => open.TargetCase switch
    {
        OpenDataChannel.TargetOneofCase.Notebook => open.Notebook.Port is > 0 and <= 65535,
        OpenDataChannel.TargetOneofCase.Terminal or OpenDataChannel.TargetOneofCase.HostFile => true,
        _ => false,
    };

    private static async Task<Stream?> ConnectAsync(TcpClient cp, AgentOptions o, OpenDataChannel open, ILogger log, CancellationToken ct)
    {
        var parts = o.ControlPlane.Split(':');
        await cp.ConnectAsync(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 7443, ct);
        var remote = await AgentTls.WrapAsync(cp.GetStream(), o, parts[0], ct);
        await Framing.WriteAsync(remote, new ConnectionHello { DataChannel = new DataChannelHello { SessionId = open.SessionId, Token = open.Token } }, ct);
        var accept = await Framing.ReadAsync(remote, DataChannelAccept.Parser, ct);
        if (accept is { Ok: true }) return remote;
        log.LogWarning("Data channel rejected: {Reason}", accept?.ErrorReason);
        await remote.DisposeAsync();
        return null;
    }

    private static async Task ServeAsync(AgentState state, OpenDataChannel open, Stream remote, ILogger log, CancellationToken ct)
    {
        switch (open.TargetCase)
        {
            case OpenDataChannel.TargetOneofCase.Notebook:
                await PipeLocalAsync(open.Notebook.Port, remote, ct);
                break;
            case OpenDataChannel.TargetOneofCase.Terminal:
                using (var pty = Terminal.TerminalSession.Open(open.Terminal, state.Options.DataPath is { Length: > 0 } d && Directory.Exists(d) ? d : state.Dir))
                {
                    log.LogInformation("Terminal session {Session} opened", open.SessionId);
                    await new Terminal.TerminalSession(remote).RunAsync(pty, ct);
                    log.LogInformation("Terminal session {Session} closed", open.SessionId);
                }
                break;
            case OpenDataChannel.TargetOneofCase.HostFile:
                await Ops.FileTransfer.RunAsync(Ops.PathGuard.Roots(state.Options, state.Dir), open.HostFile, remote, ct);
                break;
        }
    }

    private static async Task PipeLocalAsync(int port, Stream remote, CancellationToken ct)
    {
        using var local = new TcpClient { NoDelay = true };
        await local.ConnectAsync("127.0.0.1", port, ct);
        var lstream = local.GetStream();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var up = CopyAsync(lstream, remote, cts.Token);
        var down = CopyAsync(remote, lstream, cts.Token);
        await Task.WhenAny(up, down);
        await cts.CancelAsync();
    }

    private static async Task CopyAsync(Stream from, Stream to, CancellationToken ct)
    {
        try { await from.CopyToAsync(to, 64 * 1024, ct); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
}
