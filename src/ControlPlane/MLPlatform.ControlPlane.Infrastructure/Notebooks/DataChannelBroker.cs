using MLPlatform.ControlPlane.Application.Common;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using MLPlatform.AgentProtocol;
using MLPlatform.ControlPlane.Application.Notebooks;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;   // Contracts.V1 also has an Environment message (catalog)
using MLPlatform.Cqrs;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>
/// Data channel (20/04 §3.2): the api sends OpenDataChannel{session, token, target} on the control channel; the agent opens a new TCP connection to :7443
/// with DataChannelHello → the api matches it by session, replies DataChannelAccept, then both sides switch to raw bytes. One-time token, expires in 30 s.
/// </summary>
public sealed class DataChannelBroker(AgentRegistry agents, ILogger<DataChannelBroker> log)
{
    private sealed record Pending(string Token, TaskCompletionSource<Stream> Ready);
    private readonly ConcurrentDictionary<string, Pending> _pending = new();

    public async Task<Stream> OpenAsync(Guid hostId, int port, CancellationToken ct)
    {
        if (!agents.TryGet(hostId, out var session)) throw new IOException("NB_HOST_LOST: the host running the notebook is offline");
        var id = Guid.NewGuid().ToString("N");
        var token = TokenService.New("mlpd_");
        var p = new Pending(token, new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously));
        _pending[id] = p;
        try
        {
            await session.Conn.SendAsync(new ControlToAgent
            {
                StreamId = session.Conn.NewStreamId(),
                OpenDataChannel = new OpenDataChannel { SessionId = id, Token = token, Notebook = new NotebookTarget { Port = port } },
            }, ct);
            return await p.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    /// <summary>Called from AgentListener when a connection starts with DataChannelHello. Holds the connection until the HTTP side closes the stream.</summary>
    public async Task AcceptAsync(DataChannelHello hello, Stream stream, CancellationToken ct)
    {
        if (!_pending.TryRemove(hello.SessionId, out var p) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(p.Token), System.Text.Encoding.UTF8.GetBytes(hello.Token)))
        {
            await Framing.WriteAsync(stream, new DataChannelAccept { Ok = false, ErrorReason = "FLEET_DATA_CHANNEL_INVALID" }, ct);
            return;
        }
        await Framing.WriteAsync(stream, new DataChannelAccept { Ok = true }, ct);
        var owned = new OwnedStream(stream);
        if (!p.Ready.TrySetResult(owned)) return;
        await owned.Closed.WaitAsync(ct).ContinueWith(_ => { }, TaskScheduler.Default);
        log.LogDebug("Data channel {Session} closed", hello.SessionId);
    }

    /// <summary>Stream handed to HttpClient; Dispose tells the listener to close the socket.</summary>
    private sealed class OwnedStream(Stream inner) : Stream
    {
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Closed => _closed.Task;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.ReadAsync(buffer, offset, count, ct);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => inner.WriteAsync(buffer, ct);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.WriteAsync(buffer, offset, count, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _closed.TrySetResult(); base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { _closed.TrySetResult(); return ValueTask.CompletedTask; }
    }
}
