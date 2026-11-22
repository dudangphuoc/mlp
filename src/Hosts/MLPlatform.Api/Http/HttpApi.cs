using MLPlatform.Api.Http.Health;
using MLPlatform.Api.Http.HostOps;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.Api.Http.OpenApi;
using MLPlatform.Api.Http.Overlay;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;

namespace MLPlatform.Api.Http;

/// <summary>Every REST route of the api; gRPC services are mapped by GrpcApi.</summary>
public static class HttpApi
{
    public static void MapHttpApi(this IEndpointRouteBuilder app, IConfiguration cfg)
    {
        app.MapNotebookProxy();
        app.MapHostOps();
        app.MapGateway();
        app.MapFeedback();
        app.MapMcp();
        app.MapHealth();
        app.MapOverlayPeers(cfg);
        app.MapInstallScripts(cfg);
        app.MapAgentBuilds();
        app.MapOpenApi();
    }
}
