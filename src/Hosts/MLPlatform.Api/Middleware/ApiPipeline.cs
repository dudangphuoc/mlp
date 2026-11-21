using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;

namespace MLPlatform.Api.Middleware;

public static class ApiPipeline
{
    public static void UseControlPlanePipeline(this WebApplication app)
    {
        app.Use((ctx, next) => ctx.RequestServices.GetRequiredService<PublicRoutes>().HandleAsync(ctx, next));
        app.UseRouting();
        app.UseMlpAuth();
        app.UseWebSockets();
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
    }
}
