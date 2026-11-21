using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Who may see which runs: workspace runs for its members; instance-level runs (model tests) for the Instance Admin.</summary>
internal static class AiRunAccess
{
    public static async Task<(Guid? Ws, bool Instance)> ScopeAsync(IReadSession db, WorkspaceAuthorizer auth, ICurrentUser user, Guid? workspaceId, CancellationToken ct)
    {
        var uid = user.RequireUser();
        if (workspaceId is { } ws) await auth.RequireAsync(ws, "dataset.read", ct);
        var admin = await db.GetUserByIdAsync(uid, ct) is { IsInstanceAdmin: true };
        return (workspaceId, admin);
    }
}
