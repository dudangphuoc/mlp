using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Security;

namespace MLPlatform.Api.Middleware;

public static class AuthMiddleware
{
    public const string Cookie = "mlp_session";

    public static IApplicationBuilder UseMlpAuth(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var token = ctx.Request.Headers.Authorization.ToString() is { Length: > 7 } h && h.StartsWith("Bearer ")
            ? h[7..]
            : ctx.Request.Cookies[Cookie];
        if (!string.IsNullOrEmpty(token))
        {
            var sp = ctx.RequestServices;
            var hash = sp.GetRequiredService<TokenService>().Hash(token);
            var session = await sp.GetRequiredService<IReadSession>().GetSessionUserAsync(hash, ctx.RequestAborted);
            if (session is { } s)
            {
                sp.GetRequiredService<CurrentUser>().SignIn(s.UserId, hash, s.WorkspaceId);
            }
        }
        await next();
    });
}
