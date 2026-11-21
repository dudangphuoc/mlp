using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Security;

namespace MLPlatform.Api.Infrastructure;

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
            var uid = await sp.GetRequiredService<IReadSession>().GetSessionUserAsync(hash, ctx.RequestAborted);
            if (uid is { } id)
            {
                sp.GetRequiredService<CurrentUser>().SignIn(id, hash);
            }
        }
        await next();
    });
}
