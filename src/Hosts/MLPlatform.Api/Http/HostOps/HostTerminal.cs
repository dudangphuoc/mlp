using System.Globalization;
using System.Net.WebSockets;
using MLPlatform.ControlPlane.Application.Fleet.Ops;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.ControlPlane.Application.Common;

namespace MLPlatform.Api.Http.HostOps;

/// <summary>`/api/hosts/{id}/terminal` WebSocket: audits open/close, opens a terminal data channel, records the session
/// to S3.</summary>
public sealed class HostTerminal(DataChannelBroker broker, TerminalSessions sessions, MLPlatform.Storage.S3Client s3, ILogger<HostTerminal> log)
{
    public async Task HandleAsync(HttpContext ctx, Guid hostId)
    {
        if (!ctx.WebSockets.IsWebSocketRequest || !HostOpsHttp.SameOrigin(ctx)) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        if (ctx.RequestServices.GetRequiredService<CurrentUser>().UserId is not { } user) { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        if (!Guid.TryParse(ctx.Request.Query["workspace_id"], out var ws)) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        var q = ctx.Request.Query;
        var session = Guid.NewGuid();
        var shell = q["shell"].ToString();
        var mediator = ctx.RequestServices.GetRequiredService<IMediator>();
        if (!await HostOpsHttp.TryAsync(ctx, () => mediator.Send(new OpenTerminalSessionCommand(ws, hostId, session, q["reason"].ToString(), shell), ctx.RequestAborted))) return;
        if (!sessions.TryAdd(session, hostId, user)) { ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests; return; }
        try { await RunAsync(ctx, mediator, ws, hostId, session, shell); }
        finally { sessions.Remove(session); }
    }

    private async Task RunAsync(HttpContext ctx, IMediator mediator, Guid ws, Guid hostId, Guid session, string shell)
    {
        int cols = Int(ctx, "cols", 120), rows = Int(ctx, "rows", 32);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Stream channel;
        try
        {
            channel = await broker.OpenAsync(hostId, new OpenDataChannel { Terminal = new TerminalTarget { Shell = shell, Cols = (uint)cols, Rows = (uint)rows } },
                "FLEET_HOST_OFFLINE", ctx.RequestAborted);
        }
        catch (Exception e) when (e is IOException or TimeoutException) { ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
        using var recorder = new TerminalRecorder(cols, rows);
        using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
        var bridge = new TerminalBridge(socket, channel, recorder);
        await using (channel) await bridge.RunAsync(ctx.RequestAborted);
        await CloseSocketAsync(socket);
        var key = await SaveRecordingAsync(ws, session, recorder);
        var seconds = (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        await mediator.Send(new CloseTerminalSessionCommand(ws, hostId, session, seconds, bridge.BytesIn, bridge.BytesOut, bridge.ExitCode, key), CancellationToken.None);
    }

    private static int Int(HttpContext ctx, string name, int fallback) =>
        int.TryParse(ctx.Request.Query[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v is > 0 and <= 1000 ? v : fallback;

    private static async Task CloseSocketAsync(WebSocket socket)
    {
        if (socket.State != WebSocketState.Open) return;
        try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None); }
        catch (Exception e) when (e is WebSocketException or IOException) { }
    }

    private async Task<string?> SaveRecordingAsync(Guid ws, Guid session, TerminalRecorder recorder)
    {
        var key = TerminalRecordings.Key(ws, session, TimeProvider.System.GetUtcNow());
        try
        {
            await s3.PutAsync(MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts, key, recorder.Finish(), "application/gzip", CancellationToken.None);
            return key;
        }
        catch (Exception e) when (e is HttpRequestException or MLPlatform.Storage.S3Exception or TaskCanceledException)
        {
            log.LogWarning("Terminal recording {Session} not saved: {Msg}", session, e.Message);
            return null;
        }
    }
}
