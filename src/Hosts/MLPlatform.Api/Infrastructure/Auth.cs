using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Security;

namespace MLPlatform.Api.Infrastructure;

/// <summary>Per-request principal: Bearer (SDK/CLI) or the `mlp_session` cookie (web); 20/06 §1.</summary>
public sealed class CurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }
    public string? SessionTokenHash { get; set; }
}

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
                var cu = sp.GetRequiredService<CurrentUser>();
                cu.UserId = id;
                cu.SessionTokenHash = hash;
            }
        }
        await next();
    });
}
