using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

// ───────────── Serving ─────────────

/// <summary>Binds features to a deployment: the gateway receives `{"entity":"c1"}` → fetches exactly these features
/// from the online store (same definitions as at training time).</summary>
public sealed record BindDeploymentFeaturesCommand(Guid WorkspaceId, Guid DeploymentId, string Entity, IReadOnlyList<string> Refs) : ICommand<BindingRow>;

public sealed class BindDeploymentFeaturesCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<BindDeploymentFeaturesCommand, BindingRow>
{
    public async ValueTask<BindingRow> Handle(BindDeploymentFeaturesCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var entity = await db.GetEntityAsync(c.WorkspaceId, c.Entity, ct) ?? throw new NotFoundException("FEATURE_ENTITY_NOT_FOUND", "Entity not found");
        foreach (var (view, _) in await FeatureLookup.ResolveAsync(db, c.WorkspaceId, c.Refs, entity.Id, ct))
            if (!view.Online) throw new PreconditionException("FEATURE_VIEW_NOT_ONLINE", $"View {view.Name} does not have online enabled");
        await db.UpsertBindingAsync(c.DeploymentId, c.WorkspaceId, entity.Id, new JsonArray(c.Refs.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()).ToJsonString(), uid, clock.UtcNow, ct);
        return (await db.GetBindingAsync(c.DeploymentId, ct))!;
    }
}
