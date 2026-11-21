using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Grpc;

/// <summary>The gRPC layer only maps messages ↔ commands/queries (20/02 §4).</summary>
internal static class Map
{
    public static Me ToProto(this MeDto m)
    {
        var me = new Me
        {
            UserId = m.UserId.ToString(), Email = m.Email, Username = m.Username,
            DisplayName = m.DisplayName, IsInstanceAdmin = m.IsInstanceAdmin,
        };
        me.Workspaces.AddRange(m.Workspaces.Select(w => new WorkspaceMembership
            { WorkspaceId = w.Id.ToString(), Slug = w.Slug, Name = w.Name, Role = w.Role }));
        return me;
    }

    public static Workspace ToWorkspace(this MembershipRow w) => new()
        { Id = w.Id.ToString(), Slug = w.Slug, Name = w.Name, Kind = w.Kind, CreatedAt = Timestamp.FromDateTimeOffset(w.CreatedAt) };

    public static void SetSessionCookie(ServerCallContext ctx, string token)
    {
        var http = ctx.GetHttpContext();
        http.Response.Cookies.Append(AuthMiddleware.Cookie, token, new CookieOptions
        {
            HttpOnly = true, Secure = http.Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/",
            Domain = PublicRoutes.Domain.Length > 0 && http.Request.Host.Host.EndsWith(PublicRoutes.Domain, StringComparison.OrdinalIgnoreCase) ? "." + PublicRoutes.Domain : null,
        });
    }
}
