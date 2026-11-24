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
        {
            Id = k.Id.ToString(), Name = k.Name ?? "", CreatedAt = Timestamp.FromDateTimeOffset(k.CreatedAt), LastUsedAt = Timestamp.FromDateTimeOffset(k.LastSeenAt),
            WorkspaceId = k.WorkspaceId?.ToString() ?? "", WorkspaceName = k.WorkspaceName ?? "",
        };

    public override async Task<ListApiKeysResponse> ListApiKeys(Empty r, ServerCallContext ctx)
    {
        var res = new ListApiKeysResponse();
        res.Keys.AddRange((await mediator.Query(new ListApiKeysQuery(), ctx.CancellationToken)).Select(Key));
        return res;
    }

    public override async Task<CreatedApiKey> CreateApiKey(CreateApiKeyRequest r, ServerCallContext ctx)
    {
        Guid? workspace = string.IsNullOrEmpty(r.WorkspaceId) ? null : Guid.TryParse(r.WorkspaceId, out var w) ? w
            : throw new ValidationException(new Dictionary<string, string> { ["workspace_id"] = "Invalid UUID" });
        var (id, token) = await mediator.Send(new CreateApiKeyCommand(r.Name, workspace), ctx.CancellationToken);
        var now = Timestamp.FromDateTimeOffset(TimeProvider.System.GetUtcNow());
        return new CreatedApiKey { Token = token, Key = new ApiKey { Id = id.ToString(), Name = r.Name.Trim(), CreatedAt = now, LastUsedAt = now, WorkspaceId = r.WorkspaceId } };
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
