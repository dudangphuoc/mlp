using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Change stage (M11 R2, R3): production requires Owner/Admin + a reason; the previous version in the same
/// stage → archived.</summary>
[RequiresPermission("model.write")]
public sealed record TransitionStageCommand(Guid WorkspaceId, Guid VersionId, string Stage, string Reason) : ICommand<ModelVersionRow>;

public sealed class TransitionStageCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<TransitionStageCommand, ModelVersionRow>
{
    private static readonly string[] Stages = ["none", "staging", "production", "archived"];

    public async ValueTask<ModelVersionRow> Handle(TransitionStageCommand c, CancellationToken ct)
    {
        if (!Stages.Contains(c.Stage)) throw new ValidationException(new Dictionary<string, string> { ["stage"] = "Invalid stage" });
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        if (c.Stage == "production")
        {
            try { await auth.RequireAsync(c.WorkspaceId, "model.promote_production", ct); }
            catch (ForbiddenException) { throw new ForbiddenException("REGISTRY_PROMOTE_FORBIDDEN", "Admin permission is required to promote to production"); }
            if (string.IsNullOrWhiteSpace(c.Reason)) throw new ValidationException(new Dictionary<string, string> { ["reason"] = "A reason is required to promote to production" });
        }
        var v = await db.GetModelVersionAsync(c.VersionId, c.WorkspaceId, ct)
                ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var now = clock.UtcNow;
        if (c.Stage is "production" or "staging")
            foreach (var old in await db.ArchiveOtherInStageAsync(v.ModelId, c.Stage, v.Id, ct))
                await db.InsertStageTransitionAsync(Ids.New(), old.Id, c.Stage, "archived", uid, $"Replaced by v{v.Version}", now, ct);
        await db.SetStageAsync(v.Id, c.Stage, ct);
        await db.InsertStageTransitionAsync(Ids.New(), v.Id, v.Stage, c.Stage, uid, c.Reason, now, ct);
        if (c.Stage == "production")
            await MLPlatform.ControlPlane.Application.Notifications.Notifier.SendAsync(db, clock, c.WorkspaceId,
                await MLPlatform.ControlPlane.Application.Notifications.Notifier.AdminsAsync(db, c.WorkspaceId, ct), "model.promoted",
                $"Model {v.ModelName} v{v.Version} promoted to production", "#/models", $"model/{v.ModelId}", "success", ct);
        return (await db.GetModelVersionAsync(v.Id, c.WorkspaceId, ct))!;
    }
}
