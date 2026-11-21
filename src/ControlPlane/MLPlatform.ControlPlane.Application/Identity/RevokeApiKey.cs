using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Revoke one of my API keys.</summary>
public sealed record RevokeApiKeyCommand(Guid Id) : ICommand<bool>;

/// <summary>Handles <see cref="RevokeApiKeyCommand"/>.</summary>
public sealed class RevokeApiKeyCommandHandler(IWriteSession db, IClock clock, ICurrentUser user) : ICommandHandler<RevokeApiKeyCommand, bool>
{
    public async ValueTask<bool> Handle(RevokeApiKeyCommand c, CancellationToken ct) =>
        await db.RevokeApiKeyAsync(c.Id, user.RequireUser(), clock.UtcNow, ct) > 0
            ? true : throw new NotFoundException("IAM_API_KEY_NOT_FOUND", "API key not found");
}
