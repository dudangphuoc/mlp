using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;

namespace MLPlatform.AgentProtocol;

/// <summary>A control channel past hello: sequential writes via a queue, a read loop, 15 s heartbeat, 3 silent
/// intervals (45 s without receiving a byte) → close (20/04 §3).</summary>
public sealed class Connection<TIn, TOut> : IAsyncDisposable
    where TIn : class, IMessage<TIn>
    where TOut : class, IMessage<TOut>
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(45);

    private readonly Stream _stream;
    private readonly MessageParser<TIn> _parser;
    private readonly Func<TOut> _heartbeat;
    private readonly ILogger _log;
    private readonly Channel<TOut> _outbox = Channel.CreateBounded<TOut>(new BoundedChannelOptions(1024)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _cts = new();
    private long _lastActivityTicks = Environment.TickCount64;
    private long _nextStreamId;

    public Connection(Stream stream, MessageParser<TIn> parser, Func<TOut> heartbeat, ILogger log, bool oddStreamIds)
    {
        _stream = stream;
        _parser = parser;
        _heartbeat = heartbeat;
        _log = log;
        _nextStreamId = oddStreamIds ? -1 : 0;
    }

    public CancellationToken Closed => _cts.Token;

    public ulong NewStreamId() => (ulong)Interlocked.Add(ref _nextStreamId, 2);

    public ValueTask SendAsync(TOut message, CancellationToken ct = default) => _outbox.Writer.WriteAsync(message, ct);

    /// <summary>Runs until the connection closes.</summary>
    public async Task RunAsync(Func<TIn, ValueTask> onMessage, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var token = linked.Token;
        var writer = WriteLoop(token);
        var heart = HeartbeatLoop(token);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var msg = await Framing.ReadAsync(_stream, _parser, token);
                if (msg is null) break;
                Interlocked.Exchange(ref _lastActivityTicks, Environment.TickCount64);
                await onMessage(msg);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or EndOfStreamException)
        {
            _log.LogInformation("Channel closed: {Reason}", e.Message);
        }
        finally
        {
            await _cts.CancelAsync();
            _outbox.Writer.TryComplete();
            await Task.WhenAll(Swallow(writer), Swallow(heart));
        }
    }

    private async Task WriteLoop(CancellationToken ct)
    {
        await foreach (var m in _outbox.Reader.ReadAllAsync(ct))
            await Framing.WriteAsync(_stream, m, ct);
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            var idle = TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastActivityTicks));
            if (idle > IdleTimeout)
            {
                _log.LogWarning("Nothing received for {Idle} → closing channel", idle);
                await _cts.CancelAsync();
                return;
            }
            await SendAsync(_heartbeat(), ct);
        }
    }

    private static async Task Swallow(Task t)
    {
        try { await t; } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        await _stream.DisposeAsync();
        _cts.Dispose();
    }

    public static Timestamp Now(TimeProvider time) => Timestamp.FromDateTimeOffset(time.GetUtcNow());
}
