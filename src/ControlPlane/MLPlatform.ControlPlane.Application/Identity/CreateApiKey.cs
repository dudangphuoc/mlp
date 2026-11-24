using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Create an API key (the mlpk_ token is shown once; the creator's permissions, optionally limited to one
/// workspace).</summary>
[Access(AccessKind.Authenticated)]
public sealed record CreateApiKeyCommand(string Name, Guid? WorkspaceId = null) : ICommand<(Guid Id, string Token)>;

/// <summary>Handles <see cref="CreateApiKeyCommand"/>.</summary>
public sealed class CreateApiKeyCommandHandler(IWriteSession db, IClock clock, ICurrentUser user, TokenService tokens, WorkspaceAuthorizer auth) : ICommandHandler<CreateApiKeyCommand, (Guid Id, string Token)>
{
    public async ValueTask<(Guid Id, string Token)> Handle(CreateApiKeyCommand c, CancellationToken ct)
    {
        var name = c.Name.Trim();
        if (name.Length is < 1 or > 60) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Must be 1–60 characters" });
        if (user.KeyWorkspaceId is not null) throw new ForbiddenException("IAM_KEY_CANNOT_CREATE_KEYS", "A workspace API key cannot create API keys");
        if (c.WorkspaceId is { } ws) await auth.RequireAsync(ws, "run.read", ct);
        var token = TokenService.New("mlpk_");
        var id = Ids.New();
        await db.InsertApiKeyAsync(id, user.RequireUser(), tokens.Hash(token), name, clock.UtcNow, c.WorkspaceId, ct);
        return (id, token);
    }
}
