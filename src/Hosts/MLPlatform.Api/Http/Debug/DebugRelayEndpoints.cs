using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Http.Debug;

/// <summary>WebSocket routes of the debug relay for code runs: developer control and data sockets, container
/// socket.</summary>
public static class DebugRelayEndpoints
{
    public static void MapDebugRelay(this IEndpointRouteBuilder app)
    {
        app.Map("/v1/debug/{run:guid}/control", (HttpContext ctx, Guid run, DebugRelay relay) =>
            AcceptAsync(ctx, run, null, async (ws, workspace) =>
            {
                if (!await relay.ControlAsync(run, ws, ctx.RequestAborted)) await StopIfRunningAsync(ctx, workspace, run);
            }));
        app.Map("/v1/debug/{run:guid}/dev/{connection:guid}", (HttpContext ctx, Guid run, Guid connection, DebugRelay relay) =>
            AcceptAsync(ctx, run, null, (ws, _) => relay.DeveloperAsync(run, connection, ws, ctx.RequestAborted)));
        app.Map("/v1/debug/{run:guid}/remote", (HttpContext ctx, Guid run, DebugRelay relay) =>
            AcceptAsync(ctx, run, WorkloadToken(ctx) ?? "", (ws, _) => relay.RemoteAsync(run, ws, ctx.RequestAborted)));
    }

    private static async Task StopIfRunningAsync(HttpContext ctx, Guid workspace, Guid run)
    {
        try
        {
            await ctx.RequestServices.GetRequiredService<IMediator>().Send(new EndDebugSessionCommand(workspace, run), CancellationToken.None);
        }
        catch (AppException) { }
    }

    private static async Task AcceptAsync(HttpContext ctx, Guid run, string? workloadToken, Func<System.Net.WebSockets.WebSocket, Guid, Task> body)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        Guid workspace;
        try
        {
            workspace = await ctx.RequestServices.GetRequiredService<IMediator>().Query(new AuthorizeRunDebugQuery(run, workloadToken), ctx.RequestAborted);
        }
        catch (AppException) { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
        using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
        await body(socket, workspace);
        if (socket.State == System.Net.WebSockets.WebSocketState.Open)
            await socket.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    private static string? WorkloadToken(HttpContext ctx) =>
        ctx.Request.Headers.Authorization.ToString() is { } h && h.StartsWith("Bearer mlpj_", StringComparison.Ordinal) ? h[7..] : null;
}
