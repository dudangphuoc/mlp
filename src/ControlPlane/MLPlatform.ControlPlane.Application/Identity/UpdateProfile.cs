using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>S-01-2: change display name.</summary>
[Access(AccessKind.Authenticated)]
public sealed record UpdateProfileCommand(string DisplayName) : ICommand<MeDto>;

/// <summary>Handles <see cref="UpdateProfileCommand"/>.</summary>
public sealed class UpdateProfileCommandHandler(IWriteSession db, ICurrentUser user) : ICommandHandler<UpdateProfileCommand, MeDto>
{
    public async ValueTask<MeDto> Handle(UpdateProfileCommand c, CancellationToken ct)
    {
        var name = c.DisplayName.Trim();
        if (name.Length is < 1 or > 80) throw new ValidationException(new Dictionary<string, string> { ["display_name"] = "Must be 1–80 characters" });
        var uid = user.RequireUser();
        await db.UpdateDisplayNameAsync(uid, name, ct);
        return await IdentityRules.LoadMe(db, uid, ct);
    }
}
