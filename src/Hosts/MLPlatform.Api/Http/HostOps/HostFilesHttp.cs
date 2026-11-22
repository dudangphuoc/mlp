using System.Buffers.Binary;
using MLPlatform.ControlPlane.Application.Fleet.Ops;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Http.HostOps;

/// <summary>`GET /api/hosts/{id}/files?workspace_id=&amp;path=` streams a file down; `PUT` with the body uploads it
/// (audited).</summary>
public sealed class HostFilesHttp(DataChannelBroker broker)
{
    public const long MaxUpload = 20L * 1024 * 1024 * 1024;
    private const ulong Missing = ulong.MaxValue;

    public async Task DownloadAsync(HttpContext ctx, Guid hostId)
    {
        if (!Guid.TryParse(ctx.Request.Query["workspace_id"], out var ws)) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        var path = ctx.Request.Query["path"].ToString();
        var mediator = ctx.RequestServices.GetRequiredService<IMediator>();
        if (!await HostOpsHttp.TryAsync(ctx, () => mediator.Query(new AuthorizeHostFileDownloadQuery(ws, hostId), ctx.RequestAborted))) return;
        await using var channel = await broker.OpenAsync(hostId, new OpenDataChannel { HostFile = new HostFileTarget { Path = path } }, "FLEET_HOST_OFFLINE", ctx.RequestAborted);
        var header = new byte[8];
        if (await channel.ReadAtLeastAsync(header, 8, throwOnEndOfStream: false, ctx.RequestAborted) < 8) { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
        var size = BinaryPrimitives.ReadUInt64BigEndian(header);
        if (size == Missing) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.ContentLength = (long)size;
        ctx.Response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(Path.GetFileName(path.Replace('\\', '/')))}";
        await channel.CopyToAsync(ctx.Response.Body, 256 * 1024, ctx.RequestAborted);
    }

    public async Task UploadAsync(HttpContext ctx, Guid hostId)
    {
        if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = MaxUpload;
        if (!Guid.TryParse(ctx.Request.Query["workspace_id"], out var ws) || ctx.Request.ContentLength is not { } size || size > MaxUpload)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        var mediator = ctx.RequestServices.GetRequiredService<IMediator>();
        HostOpsReply? reply = null;
        var ok = await HostOpsHttp.TryAsync(ctx, async () => reply = await mediator.Send(
            new UploadHostFileCommand(ws, hostId, ctx.Request.Query["path"].ToString(), ctx.Request.Body, size), ctx.RequestAborted));
        if (!ok || reply is null) return;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync($"{{\"sha256\":\"{reply.Sha256}\",\"size\":{reply.Size}}}", ctx.RequestAborted);
    }
}
