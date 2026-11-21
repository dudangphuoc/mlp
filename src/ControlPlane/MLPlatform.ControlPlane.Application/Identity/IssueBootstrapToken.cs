using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>When the DB has no users: generate a one-time bootstrap token (24 hours) and return the raw token to print
/// to the log.</summary>
public sealed record IssueBootstrapTokenCommand : ICommand<string?>;

public sealed class IssueBootstrapTokenCommandHandler(IWriteSession db, IClock clock, TokenService tokens)
    : ICommandHandler<IssueBootstrapTokenCommand, string?>
{
    public async ValueTask<string?> Handle(IssueBootstrapTokenCommand c, CancellationToken ct)
    {
        var s = await db.GetInstanceSettingsAsync(ct);
        if (s?.BootstrappedAt is not null || await db.CountUsersAsync(ct) > 0) return null;
        var token = TokenService.New("mlpb_");
        await db.SetBootstrapTokenAsync(tokens.Hash(token), clock.UtcNow.AddHours(24), ct);
        return token;
    }
}
