using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;

namespace MLPlatform.Api.Http.Notebooks;

/// <summary>Jupyter proxy over the agent data channel (notebook owner only); not an RPC.</summary>
public static class NotebookProxyEndpoint
{
    public static void MapNotebookProxy(this IEndpointRouteBuilder app) =>
        app.Map("/nb/{id:guid}/{**rest}", (HttpContext ctx, Guid id, NotebookProxy proxy) => proxy.HandleAsync(ctx, id));
}
