using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

// ---- Workspace ----
[Access(AccessKind.Authenticated)]
public sealed record CreateWorkspaceCommand(string Slug, string Name) : ICommand<MembershipRow>;

public sealed class CreateWorkspaceCommandHandler(IWriteSession db, IClock clock, ICurrentUser user)
    : ICommandHandler<CreateWorkspaceCommand, MembershipRow>
{
    public async ValueTask<MembershipRow> Handle(CreateWorkspaceCommand c, CancellationToken ct)
    {
        var uid = user.RequireUser();
        if (!IdentityRules.Slug().IsMatch(c.Slug))
            throw new ValidationException(new Dictionary<string, string> { ["slug"] = "Invalid slug" });
        if (await db.SlugExistsAsync(c.Slug, ct))
            throw new ConflictException("IAM_SLUG_TAKEN", "URL is already in use");
        var now = clock.UtcNow;
        var id = Ids.New();
        await db.InsertWorkspaceAsync(id, c.Slug, c.Name, "team", uid, now, ct);
        await db.InsertMembershipAsync(id, uid, "owner", now, ct);
        return new MembershipRow(id, c.Slug, c.Name, "team", now, "owner");
    }
}
