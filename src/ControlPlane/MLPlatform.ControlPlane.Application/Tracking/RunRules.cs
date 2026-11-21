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

/// <summary>Rules shared by run commands: what counts as finished, and the workspace check every command starts
/// with.</summary>
public static class RunRules
{
    public static bool Finished(string state) => state is "succeeded" or "failed" or "cancelled";

    internal static async Task<RunRow> RequireAsync(IReadSession db, WorkspaceAuthorizer auth, Guid ws, Guid runId, string permission, CancellationToken ct)
    {
        await auth.RequireAsync(ws, permission, ct);
        var r = await db.GetRunAsync(runId, ct);
        return r is not null && r.WorkspaceId == ws ? r : throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
    }
}
