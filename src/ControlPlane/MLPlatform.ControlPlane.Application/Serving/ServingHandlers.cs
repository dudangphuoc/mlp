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

/// <summary>
/// Deploy model version (M12 §5.1, R1, R8): replica = `serve` job running `mlp-serve` in the recipe's image; the model directory is downloaded via
/// presigned GET (user = container); the handler comes from the recipe's `entrypoints.serve.handler`.
/// </summary>
public sealed record CreateDeploymentCommand(Guid WorkspaceId, string Slug, Guid ModelVersionId, double Cpus, int Gpus, string? ImageOverride,
    string Purpose = "service", int? IdleStopMin = null) : ICommand<DeploymentRow>;

public sealed class CreateDeploymentCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth, IMediator mediator)
    : ICommandHandler<CreateDeploymentCommand, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(CreateDeploymentCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        if (!IdentityRules.Slug().IsMatch(c.Slug)) throw new ValidationException(new Dictionary<string, string> { ["slug"] = "Invalid slug" });
        var v = await db.GetModelVersionAsync(c.ModelVersionId, c.WorkspaceId, ct)
                ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var prefix = (await db.GetVersionPrefixAsync(v.Id, c.WorkspaceId, ct))!;
        var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, v.Recipe), ct);
        var manifest = JsonNode.Parse(recipe.Manifest)!.AsObject();
        var handler = manifest["entrypoints"]?["serve"]?["handler"]?.ToString()
                      ?? throw new PreconditionException("SERVE_NOT_SUPPORTED", $"Recipe {recipe.Name} does not declare entrypoints.serve (M12 R8)");
        // Same rule as training: builtin recipes are built on the machine that runs the replica; only an invalid manifest is refused.
        var image = !string.IsNullOrEmpty(c.ImageOverride) ? c.ImageOverride
            : recipe.State != "invalid" ? $"recipes/{recipe.Name}:current"
            : throw new PreconditionException("CATALOG_RECIPE_NOT_VERIFIED", $"Recipe {recipe.Name} is not valid ({recipe.State})");
        var modelFiles = await ModelFiles.BuildAsync(db, s3, v.Id, c.WorkspaceId, ct);
        // "By recipe": an optimized version runs on the device it was built and checked for; any other follows its recipe.
        var gpus = c.Gpus >= 0 ? c.Gpus : await db.GetVersionDeviceAsync(v.Id, c.WorkspaceId, ct) switch
        {
            "cuda" => 1,
            "cpu" => 0,
            _ => manifest["environment"]?["gpu"]?.ToString() == "required" ? 1 : 0,
        };
        var cpus = c.Cpus <= 0 ? 1 : c.Cpus;
        var spec = new JobSpecDto
        {
            Image = image, Command = ["mlp-serve"], Cpus = cpus, Gpus = gpus, TimeoutSeconds = int.MaxValue,
            Env = new Dictionary<string, string>
            {
                ["MLP_HANDLER"] = handler, ["MLP_TASK"] = v.Task, ["MLP_MODEL_FILES"] = modelFiles, ["MLP_DEPLOYMENT"] = c.Slug,
                ["MLP_MODEL_VERSION"] = v.Id.ToString(),
            },
        };
        // The name of a stopped deployment can be used again: it restarts that deployment (same id, keys and public address).
        if (await db.GetDeploymentBySlugAsync(c.WorkspaceId, c.Slug, ct) is { } old)
        {
            if (old.State is not ("stopped" or "failed")) throw new ConflictException("SERVE_SLUG_TAKEN", "Deployment name is already in use");
            var restartJob = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "serve", $"serve-{c.Slug}", spec, 70), ct);
            await db.RestartDeploymentAsync(old.Id, c.WorkspaceId, v.Id, v.ModelName, v.Version, v.Task, restartJob, c.Purpose, c.IdleStopMin,
                cpus, c.Gpus >= 0 ? c.Gpus : null, c.ImageOverride, ct);
            return (await db.GetDeploymentAsync(old.Id, c.WorkspaceId, ct))!;
        }
        var id = Ids.New();
        try
        {
            var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "serve", $"serve-{c.Slug}", spec, 70), ct);
            await db.InsertDeploymentAsync(id, c.WorkspaceId, c.Slug, v.Id, v.ModelName, v.Version, v.Task, jobId, uid, clock.UtcNow, c.Purpose, c.IdleStopMin,
                cpus, c.Gpus >= 0 ? c.Gpus : null, c.ImageOverride, ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        {
            throw new ConflictException("SERVE_SLUG_TAKEN", "Deployment name is already in use");
        }
        return (await db.GetDeploymentAsync(id, c.WorkspaceId, ct))!;
    }
}

public sealed record ListDeploymentsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<DeploymentRow>>;

public sealed class ListDeploymentsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListDeploymentsQuery, IReadOnlyList<DeploymentRow>>
{
    public async ValueTask<IReadOnlyList<DeploymentRow>> Handle(ListDeploymentsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListDeploymentsAsync(q.WorkspaceId, ct);
    }
}

public sealed record GetDeploymentQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<DeploymentRow>;

public sealed class GetDeploymentQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetDeploymentQuery, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(GetDeploymentQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
    }
}

public sealed record CreateEndpointKeyCommand(Guid WorkspaceId, Guid DeploymentId, string Name) : ICommand<(Guid Id, string Key, string Prefix)>;

public sealed class CreateEndpointKeyCommandHandler(IWriteSession db, IClock clock, TokenService tokens, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateEndpointKeyCommand, (Guid Id, string Key, string Prefix)>
{
    public async ValueTask<(Guid Id, string Key, string Prefix)> Handle(CreateEndpointKeyCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var key = TokenService.New("mlpd_");
        var id = Ids.New();
        await db.InsertEndpointKeyAsync(id, c.DeploymentId, string.IsNullOrWhiteSpace(c.Name) ? "default" : c.Name, key[..9], tokens.Hash(key), uid, clock.UtcNow, ct);
        return (id, key, key[..9]);
    }
}

/// <summary>Gateway (no user): mlpd_ key → deployment by slug; returns the replica endpoint if healthy.</summary>
public sealed record ResolveEndpointQuery(string Key, Guid WorkspaceId, string Slug, bool RequireHealthy = true) : IQuery<DeploymentRow>;

public sealed class ResolveEndpointQueryHandler(IReadSession db, TokenService tokens) : IQueryHandler<ResolveEndpointQuery, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(ResolveEndpointQuery q, CancellationToken ct)
    {
        var dep = await db.GetDeploymentBySlugAsync(q.WorkspaceId, q.Slug, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var keyDep = await db.GetKeyDeploymentAsync(tokens.Hash(q.Key), ct);
        if (keyDep != dep.Id) throw new UnauthenticatedException("SERVE_KEY_INVALID", "Invalid endpoint key");
        if (q.RequireHealthy && (dep.State != "healthy" || dep.Endpoint is null)) throw new UnavailableException("SERVE_NOT_READY", $"Deployment is {dep.State}");
        return dep;
    }
}

/// <summary>Internal: replica reports endpoint / health / exit.</summary>
public sealed record SetDeploymentStateByJobCommand(Guid JobId, string State, string? Reason, string? Endpoint) : ICommand<bool>;

public sealed class SetDeploymentStateByJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetDeploymentStateByJobCommand, bool>
{
    public async ValueTask<bool> Handle(SetDeploymentStateByJobCommand c, CancellationToken ct)
    {
        if (await db.GetDeploymentByJobAsync(c.JobId, ct) is not { } d) return false;
        if (d.State == "stopped") return true;
        await db.SetDeploymentStateAsync(d.Id, c.State, c.Reason, c.Endpoint, ct);
        if (c.State == "failed" && d.State != "failed") await DeploymentAlerts.SendAsync(db, clock, d.Id, "deployment.failed", "failed", "error", ct);
        return true;
    }
}

public sealed record SetDeploymentHealthCommand(Guid DeploymentId, string State, string? Reason) : ICommand<bool>;

public sealed class SetDeploymentHealthCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetDeploymentHealthCommand, bool>
{
    public async ValueTask<bool> Handle(SetDeploymentHealthCommand c, CancellationToken ct)
    {
        await db.SetDeploymentStateAsync(c.DeploymentId, c.State, c.Reason, null, ct);
        if (c.State == "degraded") await DeploymentAlerts.SendAsync(db, clock, c.DeploymentId, "deployment.degraded", "degraded (replica not responding)", "warning", ct);
        return true;
    }
}

internal static class DeploymentAlerts
{
    public static async Task SendAsync(IWriteSession db, IClock clock, Guid deploymentId, string kind, string what, string severity, CancellationToken ct)
    {
        if (await MLPlatform.ControlPlane.Application.Notifications.NotifySql.DeploymentInfoAsync(db, deploymentId, ct) is not { } d) return;
        var to = (await MLPlatform.ControlPlane.Application.Notifications.Notifier.AdminsAsync(db, d.WorkspaceId, ct)).Append(d.CreatedBy);
        await MLPlatform.ControlPlane.Application.Notifications.Notifier.SendAsync(db, clock, d.WorkspaceId, to, kind,
            $"Deployment {d.Slug} {what}", "#/deployments", $"deployment/{d.Id}", severity, ct);
    }
}

public sealed record ListProbeTargetsQuery : IQuery<IReadOnlyList<DeploymentRow>>;

public sealed class ListProbeTargetsQueryHandler(IReadSession db) : IQueryHandler<ListProbeTargetsQuery, IReadOnlyList<DeploymentRow>>
{
    public async ValueTask<IReadOnlyList<DeploymentRow>> Handle(ListProbeTargetsQuery q, CancellationToken ct) => await db.ListDeployingWithEndpointAsync(ct);
}

public sealed record StopDeploymentCommand(Guid WorkspaceId, Guid DeploymentId) : ICommand<DeploymentRow>;

public sealed class StopDeploymentCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<StopDeploymentCommand, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(StopDeploymentCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var d = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        await db.SetDeploymentStateAsync(d.Id, "stopped", "Stopped by user", null, ct);
        return d with { State = "stopped" };
    }
}

// ───────────── Model playground: test replicas + calls with the user's session ─────────────

/// <summary>
/// "Test model": reuse a running replica of the model version, or start a test replica that stops itself after
/// <see cref="TestReplica.IdleStopMin"/> minutes without calls. Gpus &lt; 0 = follow the recipe (environment.gpu: required → 1 GPU).
/// </summary>
public sealed record StartTestDeploymentCommand(Guid WorkspaceId, Guid ModelVersionId, int Gpus, string? ImageOverride) : ICommand<DeploymentRow>;

public static class TestReplica
{
    public const int IdleStopMin = 30;
}

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

/// <summary>Replica the playground may call: any deployment the user can read, as long as it is healthy.</summary>
public sealed record ResolveTestTargetQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<DeploymentRow>;

public sealed class ResolveTestTargetQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ResolveTestTargetQuery, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(ResolveTestTargetQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var d = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        if (d.State != "healthy" || d.Endpoint is null)
            throw new UnavailableException("SERVE_NOT_READY", $"The replica is not ready yet ({d.State})");
        return d;
    }
}

/// <summary>Marks a replica as used now (keeps a test replica alive). Internal: no audit row per playground call.</summary>
public sealed record TouchDeploymentCommand(Guid DeploymentId) : ICommand<bool>, IInternalCommand;

public sealed class TouchDeploymentCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<TouchDeploymentCommand, bool>
{
    public async ValueTask<bool> Handle(TouchDeploymentCommand c, CancellationToken ct)
    {
        await db.TouchDeploymentAsync(c.DeploymentId, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>Test replicas idle past their limit; the prober stops them (state + job).</summary>
public sealed record ListIdleTestDeploymentsQuery : IQuery<IReadOnlyList<DeploymentRow>>;

public sealed class ListIdleTestDeploymentsQueryHandler(IReadSession db, IClock clock) : IQueryHandler<ListIdleTestDeploymentsQuery, IReadOnlyList<DeploymentRow>>
{
    public async ValueTask<IReadOnlyList<DeploymentRow>> Handle(ListIdleTestDeploymentsQuery q, CancellationToken ct) =>
        await db.ListIdleTestDeploymentsAsync(clock.UtcNow, ct);
}

public sealed record StopIdleDeploymentCommand(Guid DeploymentId, int IdleMinutes) : ICommand<bool>, IInternalCommand;

public sealed class StopIdleDeploymentCommandHandler(IWriteSession db) : ICommandHandler<StopIdleDeploymentCommand, bool>
{
    public async ValueTask<bool> Handle(StopIdleDeploymentCommand c, CancellationToken ct)
    {
        await db.SetDeploymentStateAsync(c.DeploymentId, "stopped", $"Stopped after {c.IdleMinutes} min without test calls", null, ct);
        return true;
    }
}

/// <summary>
/// Model files of a version as [{path, url}]: a reference version (imported by address) is read from its sources, otherwise from our storage.
/// Links last 24 hours, so the scheduler signs them again for every replica start.
/// </summary>
public static class ModelFiles
{
    public static async Task<string> BuildAsync(IReadSession db, S3Client s3, Guid versionId, Guid workspaceId, CancellationToken ct)
    {
        var refs = await db.ListVersionFileRefsAsync(versionId, ct);
        if (refs.Count > 0)
            return new JsonArray(refs.Select(f => (JsonNode)new JsonObject { ["path"] = "weights/" + f.Path, ["url"] = SourceUrls.Resolve(s3, f.Url!) }).ToArray()).ToJsonString();
        var prefix = await db.GetVersionPrefixAsync(versionId, workspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        return new JsonArray((await s3.ListAsync(Buckets.Artifacts, prefix, ct)).Select(f => (JsonNode)new JsonObject
        {
            ["path"] = f.Key[prefix.Length..],
            ["url"] = s3.Presign(HttpMethod.Get, Buckets.Artifacts, f.Key, TimeSpan.FromHours(24), Audience.Workload),
        }).ToArray()).ToJsonString();
    }
}

/// <summary>Internal (scheduler, at replica launch): fresh model links for the version.</summary>
public sealed record ResolveModelFilesQuery(Guid WorkspaceId, Guid ModelVersionId) : IQuery<string>;

/// <summary>Builds the version's model links.</summary>
public sealed class ResolveModelFilesQueryHandler(IReadSession db, S3Client s3) : IQueryHandler<ResolveModelFilesQuery, string>
{
    public async ValueTask<string> Handle(ResolveModelFilesQuery q, CancellationToken ct) => await ModelFiles.BuildAsync(db, s3, q.ModelVersionId, q.WorkspaceId, ct);
}

/// <summary>Start a stopped or failed deployment again, on the same or another version of its model; same machine, keys and address.</summary>
public sealed record StartDeploymentCommand(Guid WorkspaceId, Guid DeploymentId, Guid? ModelVersionId) : ICommand<DeploymentRow>;

/// <summary>Re-submits the replica through <see cref="CreateDeploymentCommand"/> with the stored machine.</summary>
public sealed class StartDeploymentCommandHandler(IReadSession db, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<StartDeploymentCommand, DeploymentRow>
{
    public async ValueTask<DeploymentRow> Handle(StartDeploymentCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var d = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        if (d.State is not ("stopped" or "failed")) throw new PreconditionException("SERVE_RUNNING", "Stop the deployment first");
        return await mediator.Send(new CreateDeploymentCommand(c.WorkspaceId, d.Slug, c.ModelVersionId ?? d.ModelVersionId, d.Cpus ?? 1, d.Gpus ?? -1,
            d.ImageOverride, d.Purpose, d.IdleStopMin), ct);
    }
}

/// <summary>Delete a stopped deployment with its keys and monitoring history; the name becomes free.</summary>
public sealed record DeleteDeploymentCommand(Guid WorkspaceId, Guid DeploymentId) : ICommand<bool>;

/// <summary>Refuses while running; otherwise removes the deployment's rows in every module.</summary>
public sealed class DeleteDeploymentCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteDeploymentCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteDeploymentCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var d = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        if (d.State is not ("stopped" or "failed")) throw new PreconditionException("SERVE_RUNNING", "Stop the deployment first");
        // Each module drops what it keeps about the deployment; one write session = one transaction.
        await MLPlatform.ControlPlane.Application.Monitoring.MonitorSql.DeleteMonitorDeploymentDataAsync(db, d.Id, ct);
        await MLPlatform.ControlPlane.Application.LlmOps.LlmSql.DeleteDeploymentDataAsync(db, d.Id, ct);
        await MLPlatform.ControlPlane.Application.Features.FeatureSql.DeleteDeploymentBindingAsync(db, d.Id, ct);
        await MLPlatform.ControlPlane.Application.Registry.RegistrySql.ForgetDeploymentAsync(db, d.Id, ct);
        await db.DeleteDeploymentAsync(d.Id, ct);
        return true;
    }
}

/// <summary>Keys of a deployment.</summary>
public sealed record ListEndpointKeysQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<IReadOnlyList<EndpointKeyRow>>;

/// <summary>Keys of one deployment, newest first (prefix only).</summary>
public sealed class ListEndpointKeysQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEndpointKeysQuery, IReadOnlyList<EndpointKeyRow>>
{
    public async ValueTask<IReadOnlyList<EndpointKeyRow>> Handle(ListEndpointKeysQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.ListEndpointKeysAsync(q.DeploymentId, ct);
    }
}

/// <summary>Revoke one endpoint key.</summary>
public sealed record RevokeEndpointKeyCommand(Guid WorkspaceId, Guid DeploymentId, Guid KeyId) : ICommand<bool>;

/// <summary>Marks the key revoked; the gateway stops accepting it at once.</summary>
public sealed class RevokeEndpointKeyCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<RevokeEndpointKeyCommand, bool>
{
    public async ValueTask<bool> Handle(RevokeEndpointKeyCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        await db.RevokeEndpointKeyAsync(c.KeyId, c.DeploymentId, clock.UtcNow, ct);
        return true;
    }
}
