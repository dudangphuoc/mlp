namespace MLPlatform.Api.Http.HostOps;

/// <summary>Remote ops routes that are not RPCs: interactive terminal (WebSocket) and file streaming.</summary>
public static class HostOpsEndpoints
{
    public static void MapHostOps(this IEndpointRouteBuilder app)
    {
        app.Map("/api/hosts/{id:guid}/terminal", (HttpContext ctx, Guid id, HostTerminal t) => t.HandleAsync(ctx, id));
        app.MapGet("/api/hosts/{id:guid}/files", (HttpContext ctx, Guid id, HostFilesHttp f) => f.DownloadAsync(ctx, id));
        app.MapPut("/api/hosts/{id:guid}/files", (HttpContext ctx, Guid id, HostFilesHttp f) => f.UploadAsync(ctx, id));
    }
}
