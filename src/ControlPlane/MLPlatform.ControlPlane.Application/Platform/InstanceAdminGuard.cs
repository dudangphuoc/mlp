using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>Only an Instance Admin may view/edit instance settings.</summary>
public sealed class InstanceAdminGuard(IReadSession db, ICurrentUser user)
{
    public async Task<Guid> RequireAsync(CancellationToken ct)
    {
        var uid = user.RequireUser();
        if (await db.GetUserByIdAsync(uid, ct) is not { IsInstanceAdmin: true })
            throw new ForbiddenException("IAM_PERMISSION_DENIED", "Only an Instance Admin can manage system settings");
        return uid;
    }
}
