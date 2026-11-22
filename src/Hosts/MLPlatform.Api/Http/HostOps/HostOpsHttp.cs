using MLPlatform.Cqrs;

namespace MLPlatform.Api.Http.HostOps;

/// <summary>Shared HTTP helpers for the remote ops endpoints.</summary>
public static class HostOpsHttp
{
    /// <summary>Cookie-authenticated WebSockets must come from this site (cross-site WebSocket hijacking).</summary>
    public static bool SameOrigin(HttpContext ctx)
    {
        var origin = ctx.Request.Headers.Origin.ToString();
        return origin.Length == 0 || (Uri.TryCreate(origin, UriKind.Absolute, out var u) && string.Equals(u.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs an application call; maps an AppException to an HTTP status with the reason code and returns
    /// false.</summary>
    public static async Task<bool> TryAsync<T>(HttpContext ctx, Func<ValueTask<T>> call)
    {
        try
        {
            await call();
            return true;
        }
        catch (AppException e)
        {
            ctx.Response.StatusCode = e switch
            {
                UnauthenticatedException => StatusCodes.Status401Unauthorized,
                ForbiddenException => StatusCodes.Status403Forbidden,
                NotFoundException => StatusCodes.Status404NotFound,
                ValidationException => StatusCodes.Status400BadRequest,
                UnavailableException => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status409Conflict,
            };
            ctx.Response.Headers["x-mlp-reason"] = e.Reason;
            return false;
        }
    }
}
