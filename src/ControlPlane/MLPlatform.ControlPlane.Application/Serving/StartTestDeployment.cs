using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Serving;

// ───────────── Model playground: test replicas + calls with the user's session ─────────────

/// <summary>"Test model": reuse a running replica of the model version, or start a test replica that stops itself after
/// IdleStopMin minutes without calls.</summary>
public sealed record StartTestDeploymentCommand(Guid WorkspaceId, Guid ModelVersionId, int Gpus, string? ImageOverride) : ICommand<DeploymentRow>;

public sealed class StartTestDeploymentCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IMediator mediator)
    : ICommandHandler<StartTestDeploymentCommand, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(StartTestDeploymentCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        if (await db.FindActiveForVersionAsync(c.WorkspaceId, c.ModelVersionId, ct) is { } running) return running;
        var v = await db.GetModelVersionAsync(c.ModelVersionId, c.WorkspaceId, ct)
                ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var slug = $"test-{IdentityRules.SlugPart(v.ModelName, 30)}-v{v.Version}-{Ids.New().ToString("N")[^4..]}";
        return await mediator.Send(new CreateDeploymentCommand(c.WorkspaceId, slug, v.Id, 1, c.Gpus, c.ImageOverride, "test", TestReplica.IdleStopMin), ct);
    }
}
