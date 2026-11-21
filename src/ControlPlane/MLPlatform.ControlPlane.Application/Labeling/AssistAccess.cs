using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Who may do what in assist: everyone in the project reads; Lead (or labeling.manage) configures and runs batches.</summary>
internal static class AssistAccess
{
    public static async Task<(Guid Uid, ProjectRow P, HashSet<string> Roles)> ManageAsync(LabelAccess access, WorkspaceAuthorizer auth, Guid ws, Guid pid, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(ws, pid, "dataset.read", ct);
        if (!roles.Contains("lead") && !await auth.HasAsync(ws, "labeling.manage", ct))
            throw new ForbiddenException("LABEL_NOT_MEMBER", "Only a Lead can change AI assist settings or run batches");
        return (uid, p, roles);
    }
}
