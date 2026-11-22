using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

/// <summary>Permission check plus "the machine belongs to this workspace" for every remote ops request.</summary>
public static class HostOpsAccess
{
    public static async Task RequireAsync(WorkspaceAuthorizer auth, IReadSession db, Guid workspaceId, Guid hostId, string permission, CancellationToken ct)
    {
        await auth.RequireAsync(workspaceId, permission, ct);
        _ = await db.GetHostAsync(hostId, workspaceId, ct) ?? throw new NotFoundException("FLEET_HOST_NOT_FOUND", "Machine not found");
    }
}
