using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Rename a run (1–80 characters).</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record RenameRunCommand(Guid WorkspaceId, Guid RunId, string Name) : ICommand<bool>;

/// <summary>Validates the name and renames the run.</summary>
public sealed class RenameRunCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<RenameRunCommand, bool>
{
    public async ValueTask<bool> Handle(RenameRunCommand c, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, "job.submit", ct);
        var name = c.Name.Trim();
        if (name.Length is 0 or > 80) throw new InvalidArgumentException("TRACK_NAME_INVALID", "Run name must be 1 to 80 characters");
        await db.RenameRunAsync(r.Id, r.WorkspaceId, name, ct);
        return true;
    }
}
