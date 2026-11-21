using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Services;

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
            // Under a public domain: the cookie is shared across *.{domain} (notebook nb-….{domain} needs the login session)
            Domain = PublicRoutes.Domain.Length > 0 && http.Request.Host.Host.EndsWith(PublicRoutes.Domain, StringComparison.OrdinalIgnoreCase) ? "." + PublicRoutes.Domain : null,
        });
    }
}

public sealed class IamGrpc(IMediator mediator) : IamService.IamServiceBase
{
    public override async Task<Me> UpdateProfile(UpdateProfileRequest r, ServerCallContext ctx) =>
        (await mediator.Send(new UpdateProfileCommand(r.DisplayName), ctx.CancellationToken)).ToProto();

    public override async Task<Empty> ChangePassword(ChangePasswordRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new ChangePasswordCommand(r.CurrentPassword, r.NewPassword), ctx.CancellationToken);
        return new Empty();
    }

    private static ApiKey Key(ApiKeyRow k) => new()
        { Id = k.Id.ToString(), Name = k.Name ?? "", CreatedAt = Timestamp.FromDateTimeOffset(k.CreatedAt), LastUsedAt = Timestamp.FromDateTimeOffset(k.LastSeenAt) };

    public override async Task<ListApiKeysResponse> ListApiKeys(Empty r, ServerCallContext ctx)
    {
        var res = new ListApiKeysResponse();
        res.Keys.AddRange((await mediator.Query(new ListApiKeysQuery(), ctx.CancellationToken)).Select(Key));
        return res;
    }

    public override async Task<CreatedApiKey> CreateApiKey(CreateApiKeyRequest r, ServerCallContext ctx)
    {
        var (id, token) = await mediator.Send(new CreateApiKeyCommand(r.Name), ctx.CancellationToken);
        var now = Timestamp.FromDateTimeOffset(TimeProvider.System.GetUtcNow());
        return new CreatedApiKey { Token = token, Key = new ApiKey { Id = id.ToString(), Name = r.Name.Trim(), CreatedAt = now, LastUsedAt = now } };
    }

    public override async Task<Empty> RevokeApiKey(RevokeApiKeyRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new RevokeApiKeyCommand(Guid.TryParse(r.Id, out var g) ? g : Guid.Empty), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<RegisterResponse> Register(RegisterRequest r, ServerCallContext ctx)
    {
        var id = await mediator.Send(new RegisterCommand(r.Email, r.Password, r.Username, r.DisplayName, r.InvitationToken), ctx.CancellationToken);
        return new RegisterResponse { UserId = id.ToString() };
    }

    public override async Task<LoginResponse> Login(LoginRequest r, ServerCallContext ctx)
    {
        var s = await mediator.Send(new LoginCommand(r.Email, r.Password), ctx.CancellationToken);
        Map.SetSessionCookie(ctx, s.Token);
        return new LoginResponse { SessionToken = s.Token, Me = s.Me.ToProto() };
    }

    public override async Task<Empty> Logout(Empty r, ServerCallContext ctx)
    {
        await mediator.Send(new LogoutCommand(), ctx.CancellationToken);
        var http = ctx.GetHttpContext();
        http.Response.Cookies.Delete(AuthMiddleware.Cookie);
        if (PublicRoutes.Domain.Length > 0) http.Response.Cookies.Delete(AuthMiddleware.Cookie, new CookieOptions { Domain = "." + PublicRoutes.Domain, Path = "/" });
        return new Empty();
    }

    public override async Task<Me> GetMe(Empty r, ServerCallContext ctx) =>
        (await mediator.Query(new GetMeQuery(), ctx.CancellationToken)).ToProto();
}

public sealed class WorkspaceGrpc(IMediator mediator) : WorkspaceService.WorkspaceServiceBase
{
    public override async Task<Empty> UpdateWorkspace(UpdateWorkspaceRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new RenameWorkspaceCommand(G(r.WorkspaceId, "workspace_id"), r.Name), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> UpdateMemberRole(UpdateMemberRoleRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new SetMemberRoleCommand(G(r.WorkspaceId, "workspace_id"), G(r.UserId, "user_id"), r.Role), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> RemoveMember(RemoveMemberRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new RemoveMemberCommand(G(r.WorkspaceId, "workspace_id"), G(r.UserId, "user_id")), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<ListInvitationsResponse> ListInvitations(ListInvitationsRequest r, ServerCallContext ctx)
    {
        var res = new ListInvitationsResponse();
        res.Invitations.AddRange((await mediator.Query(new ListInvitationsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(i => new InvitationInfo
        {
            Id = i.Id.ToString(), Email = i.Email, Role = i.Role, CreatedAt = Timestamp.FromDateTimeOffset(i.CreatedAt),
            ExpiresAt = Timestamp.FromDateTimeOffset(i.ExpiresAt), AcceptedAt = i.AcceptedAt is { } a ? Timestamp.FromDateTimeOffset(a) : null,
        }));
        return res;
    }

    public override async Task<Empty> RevokeInvitation(RevokeInvitationRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new RevokeInvitationCommand(G(r.WorkspaceId, "workspace_id"), G(r.Id, "id")), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<ListWorkspacesResponse> ListWorkspaces(Empty r, ServerCallContext ctx)
    {
        var res = new ListWorkspacesResponse();
        res.Workspaces.AddRange((await mediator.Query(new ListWorkspacesQuery(), ctx.CancellationToken)).Select(Map.ToWorkspace));
        return res;
    }

    public override async Task<Workspace> CreateWorkspace(CreateWorkspaceRequest r, ServerCallContext ctx) =>
        (await mediator.Send(new CreateWorkspaceCommand(r.Slug, r.Name), ctx.CancellationToken)).ToWorkspace();

    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<ListMembersResponse> ListMembers(ListMembersRequest r, ServerCallContext ctx)
    {
        var res = new ListMembersResponse();
        res.Members.AddRange((await mediator.Query(new ListMembersQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(m => new Member
            { UserId = m.UserId.ToString(), Username = m.Username, DisplayName = m.DisplayName, Email = m.Email, Role = m.Role }));
        return res;
    }

    public override async Task<Invitation> CreateInvitation(CreateInvitationRequest r, ServerCallContext ctx)
    {
        var (token, exp) = await mediator.Send(new CreateInvitationCommand(G(r.WorkspaceId, "workspace_id"), r.Email, r.Role), ctx.CancellationToken);
        return new Invitation { Token = token, ExpiresAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(exp) };
    }
}

public sealed class InstanceGrpc(IMediator mediator) : InstanceService.InstanceServiceBase
{
    public override async Task<SetupState> GetSetupState(Empty r, ServerCallContext ctx) =>
        new() { SetupRequired = await mediator.Query(new GetSetupStateQuery(), ctx.CancellationToken) };

    public override async Task<Me> CompleteSetup(CompleteSetupRequest r, ServerCallContext ctx)
    {
        var s = await mediator.Send(new CompleteSetupCommand(r.BootstrapToken, r.Email, r.Password, r.Username,
            r.DisplayName, r.InstanceName, r.AllowSignup), ctx.CancellationToken);
        Map.SetSessionCookie(ctx, s.Token);
        return s.Me.ToProto();
    }
}
