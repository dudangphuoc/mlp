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

namespace MLPlatform.Api.Infrastructure;

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

/// <summary>
/// HTTP `/nb/{id}/…` (M08 §5 step 2, R2): notebook owner only (others → 404); forwards HTTP + WebSocket over the data channel to
/// 127.0.0.1:&lt;port&gt; on the host; the api attaches the Jupyter token itself and does not forward the platform session cookie.
/// </summary>
public sealed class NotebookProxy
{
    private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection", "TE", "Trailer", "Host", "Authorization", "Cookie",
        "Sec-WebSocket-Key", "Sec-WebSocket-Version", "Sec-WebSocket-Extensions", "Sec-WebSocket-Protocol",
    };

    private readonly HttpMessageInvoker _invoker;
    private readonly IServiceScopeFactory _scopes;
    private readonly TokenService _tokens;
    private readonly ConcurrentDictionary<Guid, long> _lastTouch = new();
    private readonly ILogger<NotebookProxy> _log;

    public NotebookProxy(DataChannelBroker broker, IServiceScopeFactory scopes, TokenService tokens, ILogger<NotebookProxy> log)
    {
        _log = log;
        _scopes = scopes;
        _tokens = tokens;
        // Virtual host name "x{hostId:N}-{port}.nb" → one connection pool per (host, port); each TCP connection = one data channel.
        _invoker = new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseProxy = false,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
            ConnectCallback = async (ctx, ct) =>
            {
                var label = ctx.DnsEndPoint.Host.Split('.')[0];
                var dash = label.LastIndexOf('-');
                return await broker.OpenAsync(Guid.ParseExact(label[1..dash], "N"), int.Parse(label[(dash + 1)..], System.Globalization.CultureInfo.InvariantCulture), ct);
            },
        });
    }

    public async Task HandleAsync(HttpContext ctx, Guid id)
    {
        var user = ctx.RequestServices.GetRequiredService<CurrentUser>();
        ServerRow? s = null;
        if (user.UserId is { } uid)
            s = await ctx.RequestServices.GetRequiredService<IMediator>().Query(new ResolveNotebookProxyQuery(id, uid), ctx.RequestAborted);
        if (s is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var port = int.Parse(s.Endpoint![(s.Endpoint!.LastIndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        var target = $"x{s.HostId!.Value:N}-{port}.nb{ctx.Request.Path}{ctx.Request.QueryString}";
        var token = NotebookRules.JupyterToken(_tokens, id);
        await TouchAsync(id);
        try
        {
            if (ctx.WebSockets.IsWebSocketRequest) await WebSocketAsync(ctx, "ws://" + target, token);
            else await HttpAsync(ctx, "http://" + target, token);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TimeoutException or WebSocketException && !ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
            await ctx.Response.WriteAsync("Could not connect to the notebook: " + e.Message);
        }
    }

    /// <summary>R4: activity marker; written at most once per 30 s, in a separate scope with no user (no audit generated).</summary>
    private async Task TouchAsync(Guid id)
    {
        var now = Environment.TickCount64;
        if (_lastTouch.TryGetValue(id, out var last) && now - last < 30_000) return;
        _lastTouch[id] = now;
        await using var scope = _scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new TouchNotebookCommand(id));
    }

    private async Task HttpAsync(HttpContext ctx, string url, string token)
    {
        using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), url);
        var hasBody = ctx.Request.ContentLength > 0 || ctx.Request.Headers.TransferEncoding.Count > 0;
        if (hasBody) req.Content = new StreamContent(ctx.Request.Body);
        foreach (var (k, v) in ctx.Request.Headers)
        {
            if (HopHeaders.Contains(k)) continue;
            if (!req.Headers.TryAddWithoutValidation(k, (IEnumerable<string>)v)) req.Content?.Headers.TryAddWithoutValidation(k, (IEnumerable<string>)v);
        }
        req.Headers.TryAddWithoutValidation("Authorization", "token " + token);
        using var res = await _invoker.SendAsync(req, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)res.StatusCode;
        foreach (var h in res.Headers.Concat(res.Content.Headers))
            if (!HopHeaders.Contains(h.Key) && !h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                ctx.Response.Headers[h.Key] = h.Value.ToArray();
        await res.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    }

    private async Task WebSocketAsync(HttpContext ctx, string url, string token)
    {
        using var upstream = new ClientWebSocket();
        upstream.Options.SetRequestHeader("Authorization", "token " + token);
        foreach (var p in ctx.WebSockets.WebSocketRequestedProtocols) upstream.Options.AddSubProtocol(p);
        await upstream.ConnectAsync(new Uri(url), _invoker, ctx.RequestAborted);
        using var down = await ctx.WebSockets.AcceptWebSocketAsync(upstream.SubProtocol);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        var a = PumpAsync(down, upstream, cts.Token);
        var b = PumpAsync(upstream, down, cts.Token);
        var first = await Task.WhenAny(a, b);
        if (first.Exception is { } ex) _log.LogDebug(ex.GetBaseException(), "Notebook WebSocket closed due to an error");
        // One side sent Close → already forwarded to the other side; wait for its Close reply to complete the closing handshake on both ends.
        await Task.WhenAny(Task.WhenAll(a, b), Task.Delay(TimeSpan.FromSeconds(5)));
        await cts.CancelAsync();
    }

    private static async Task<string> PumpAsync(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var r = await from.ReceiveAsync(buf, ct);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    if (to.State is WebSocketState.Open or WebSocketState.CloseReceived)
                        await to.CloseOutputAsync(r.CloseStatus ?? WebSocketCloseStatus.NormalClosure, r.CloseStatusDescription, ct);
                    return $"close {r.CloseStatus}";
                }
                await to.SendAsync(buf.AsMemory(0, r.Count), r.MessageType, r.EndOfMessage, ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException) { return e.GetType().Name; }
        return "ct";
    }
}

/// <summary>R4: every minute; warn 10 minutes ahead (M16), idle past idle_timeout → stop.</summary>
public sealed class NotebookReaper(IServiceScopeFactory scopes, WorkloadLauncher launcher, SystemHealth health, ILogger<NotebookReaper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("notebook-reaper", 60);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var m = scope.ServiceProvider.GetRequiredService<IMediator>();
                await m.Send(new WarnIdleNotebooksCommand(), stop);
                foreach (var job in await m.Send(new ReconcileStoppingNotebooksCommand(), stop))
                    try { await launcher.StopJobAsync(job, stop); }
                    catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Resending the stop for job {Job} failed", job); }
                foreach (var s in await m.Query(new ListIdleNotebooksQuery(), stop))
                {
                    await m.Send(new MarkNotebookStoppingCommand(s.Id, $"Stopped automatically after {s.IdleTimeoutMin} minutes idle"), stop);
                    if (s.JobId is { } job) await launcher.StopJobAsync(job, stop);
                    log.LogInformation("Notebook {Id} idle for over {Min} minutes → stopping", s.Id, s.IdleTimeoutMin);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Idle notebook cleanup failed");
            }
        }
    }
}
