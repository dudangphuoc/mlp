using System.Net.WebSockets;
using MLPlatform.AgentProtocol;

namespace MLPlatform.Api.Http.HostOps;

/// <summary>Pipes a browser WebSocket ([u8 kind][payload] per message) to an agent data channel (ChannelFrame) and
/// back, recording both directions; closes after 30 minutes without I/O.</summary>
public sealed class TerminalBridge(WebSocket ws, Stream channel, TerminalRecorder recorder)
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);
    private long _lastIo = System.Diagnostics.Stopwatch.GetTimestamp();

    public long BytesIn { get; private set; }
    public long BytesOut { get; private set; }
    public int? ExitCode { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var up = UpAsync(cts.Token);
        var down = DownAsync(cts.Token);
        var idle = IdleAsync(cts.Token);
        await Task.WhenAny(up, down, idle);
        await cts.CancelAsync();
    }

    private async Task UpAsync(CancellationToken ct)
    {
        var buf = new byte[ChannelFrame.MaxPayload + 1];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buf, ct);
                if (r.MessageType == WebSocketMessageType.Close || r.Count == 0 || !r.EndOfMessage) return;
                var kind = (ChannelFrameKind)buf[0];
                var payload = buf.AsMemory(1, r.Count - 1);
                if (kind == ChannelFrameKind.Data) { BytesIn += payload.Length; recorder.Input(payload.Span); }
                Touch();
                await ChannelFrame.WriteAsync(channel, kind, payload, ct);
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException or InvalidDataException) { }
    }

    private async Task DownAsync(CancellationToken ct)
    {
        try
        {
            while (await ChannelFrame.ReadAsync(channel, ct) is { } f)
            {
                if (f.Kind == ChannelFrameKind.Data) { BytesOut += f.Payload.Length; recorder.Output(f.Payload.Span); }
                if (f.Kind == ChannelFrameKind.Exit && f.Payload.Length == 4) ExitCode = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(f.Payload.Span);
                Touch();
                await ws.SendAsync((byte[])[(byte)f.Kind, .. f.Payload.Span], WebSocketMessageType.Binary, true, ct);
                if (f.Kind == ChannelFrameKind.Exit) return;
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException or InvalidDataException) { }
    }

    private void Touch() => Interlocked.Exchange(ref _lastIo, System.Diagnostics.Stopwatch.GetTimestamp());

    private async Task IdleAsync(CancellationToken ct)
    {
        try
        {
            while (System.Diagnostics.Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastIo)) < IdleLimit)
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
        catch (OperationCanceledException) { }
    }
}
