using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Application.Common;

namespace MLPlatform.Api.Http.Mcp;

/// <summary>Model Context Protocol endpoint for external AI agents (Bearer API key, JSON responses only).</summary>
public static class McpEndpoints
{
    public static void MapMcp(this IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp", (HttpContext h, McpServer mcp, CurrentUser cu) => mcp.HandleAsync(h, cu.UserId));
        app.MapGet("/mcp", (HttpContext h) => { h.Response.StatusCode = 405; return Task.CompletedTask; });
    }
}
