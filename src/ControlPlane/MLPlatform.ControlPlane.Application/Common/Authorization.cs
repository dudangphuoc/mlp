using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Permission matrix M01 §4.1 (implemented part). No read permission → NOT_FOUND; read but no write → PERMISSION_DENIED.</summary>
public static class Permissions
{
    private static readonly Dictionary<string, string[]> Matrix = new()
    {
        ["workspace.manage"] = ["owner"],
        ["member.manage"] = ["owner", "admin"],
        ["fleet.manage"] = ["owner", "admin"],
        ["fleet.read"] = ["owner", "admin", "editor", "viewer"],
        ["dataset.write"] = ["owner", "admin", "editor"],
        ["dataset.read"] = ["owner", "admin", "editor", "reviewer", "annotator", "viewer"],
        ["job.submit"] = ["owner", "admin", "editor"],
        ["model.write"] = ["owner", "admin", "editor"],
        ["model.promote_production"] = ["owner", "admin"],
        ["run.read"] = ["owner", "admin", "editor", "viewer"],
        ["deployment.manage"] = ["owner", "admin", "editor"],
        ["audit.read"] = ["owner", "admin"],
        ["notebook.use"] = ["owner", "admin", "editor"],
        ["notebook.admin"] = ["owner", "admin"],
        ["labeling.manage"] = ["owner", "admin", "editor"],
        ["labeling.annotate"] = ["owner", "admin", "editor", "reviewer", "annotator"],
    };

    public static bool Allows(string role, string permission) =>
        Matrix.TryGetValue(permission, out var roles) && roles.Contains(role);
}

public sealed class WorkspaceAuthorizer(IReadSession db, ICurrentUser user)
{
    /// <summary>Returns userId if permitted; throws NotFound when not a member, Forbidden when lacking the permission.</summary>
    public async Task<Guid> RequireAsync(Guid workspaceId, string permission, CancellationToken ct)
    {
        var uid = user.RequireUser();
        var role = await db.GetMemberRoleAsync(workspaceId, uid, ct)
                   ?? throw new NotFoundException("IAM_WORKSPACE_NOT_FOUND", "Workspace not found");
        if (!Permissions.Allows(role, permission))
            throw new ForbiddenException("IAM_PERMISSION_DENIED", $"Missing permission {permission}");
        return uid;
    }

    /// <summary>Whether the permission is held (does not throw) — used for wider view scopes such as the Admin's "All".</summary>
    public async Task<bool> HasAsync(Guid workspaceId, string permission, CancellationToken ct) =>
        user.UserId is { } uid && await db.GetMemberRoleAsync(workspaceId, uid, ct) is { } role && Permissions.Allows(role, permission);
}
