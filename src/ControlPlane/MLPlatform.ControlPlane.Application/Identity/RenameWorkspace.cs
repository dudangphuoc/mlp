using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>S-01-5: rename the workspace (Owner).</summary>
[RequiresPermission("workspace.manage")]
public sealed record RenameWorkspaceCommand(Guid WorkspaceId, string Name) : ICommand<bool>;

/// <summary>Handles <see cref="RenameWorkspaceCommand"/>.</summary>
public sealed class RenameWorkspaceCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<RenameWorkspaceCommand, bool>
{
    public async ValueTask<bool> Handle(RenameWorkspaceCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "workspace.manage", ct);
        var name = c.Name.Trim();
        if (name.Length is < 1 or > 80) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Must be 1–80 characters" });
        await db.RenameWorkspaceAsync(c.WorkspaceId, name, ct);
        return true;
    }
}
