using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Create an API key (the mlpk_ token is shown once; same permissions as the creator).</summary>
public sealed record CreateApiKeyCommand(string Name) : ICommand<(Guid Id, string Token)>;

/// <summary>Handles <see cref="CreateApiKeyCommand"/>.</summary>
public sealed class CreateApiKeyCommandHandler(IWriteSession db, IClock clock, ICurrentUser user, TokenService tokens) : ICommandHandler<CreateApiKeyCommand, (Guid Id, string Token)>
{
    public async ValueTask<(Guid Id, string Token)> Handle(CreateApiKeyCommand c, CancellationToken ct)
    {
        var name = c.Name.Trim();
        if (name.Length is < 1 or > 60) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Must be 1–60 characters" });
        var token = TokenService.New("mlpk_");
        var id = Ids.New();
        await db.InsertApiKeyAsync(id, user.RequireUser(), tokens.Hash(token), name, clock.UtcNow, ct);
        return (id, token);
    }
}
