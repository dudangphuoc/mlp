using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

public sealed class WorkspaceAuthorizer(IReadSession db, ICurrentUser user)
{
    private readonly Dictionary<(Guid Workspace, Guid User), string> _roles = [];

    /// <summary>Returns userId if permitted; throws NotFound when not a member, Forbidden when lacking the
    /// permission.</summary>
    public async Task<Guid> RequireAsync(Guid workspaceId, string permission, CancellationToken ct)
    {
        var uid = user.RequireUser();
        if (!KeyAllows(workspaceId)) throw new NotFoundException("IAM_WORKSPACE_NOT_FOUND", "Workspace not found");
        var role = await RoleAsync(workspaceId, uid, ct)
                   ?? throw new NotFoundException("IAM_WORKSPACE_NOT_FOUND", "Workspace not found");
        if (!Permissions.Allows(role, permission))
            throw new ForbiddenException("IAM_PERMISSION_DENIED", $"Missing permission {permission}");
        return uid;
    }

    /// <summary>Whether the permission is held (does not throw) — used for wider view scopes such as the Admin's
    /// "All".</summary>
    public async Task<bool> HasAsync(Guid workspaceId, string permission, CancellationToken ct) =>
        user.UserId is { } uid && KeyAllows(workspaceId) && await RoleAsync(workspaceId, uid, ct) is { } role && Permissions.Allows(role, permission);

    private bool KeyAllows(Guid workspaceId) => user.KeyWorkspaceId is not { } only || only == workspaceId;

    private async Task<string?> RoleAsync(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        if (_roles.TryGetValue((workspaceId, userId), out var cached)) return cached;
        var role = await db.GetMemberRoleAsync(workspaceId, userId, ct);
        if (role is not null) _roles[(workspaceId, userId)] = role;
        return role;
    }
}
