using MLPlatform.ControlPlane.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Services;

public sealed class ServingGrpc(IMediator mediator, MLPlatform.ControlPlane.Infrastructure.WorkloadLauncher launcher, MLPlatform.Api.Infrastructure.PublicRoutes routes,
    MLPlatform.ControlPlane.Infrastructure.ModelServerClients clients) : ServingService.ServingServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<Deployment> CreateDeployment(CreateDeploymentRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateDeploymentCommand(G(r.WorkspaceId, "workspace_id"), r.Slug, G(r.ModelVersionId, "model_version_id"),
            r.Cpus, r.Gpus, string.IsNullOrEmpty(r.ImageOverride) ? null : r.ImageOverride), ctx.CancellationToken));

    public override async Task<ListDeploymentsResponse> ListDeployments(ListDeploymentsRequest r, ServerCallContext ctx)
    {
        var res = new ListDeploymentsResponse();
        foreach (var d in await mediator.Query(new ListDeploymentsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken))
            res.Deployments.Add(await WithUrl(ToProto(d), ctx.CancellationToken));
        return res;
    }

    public override async Task<Deployment> GetDeployment(GetDeploymentRequest r, ServerCallContext ctx) =>
        await WithUrl(ToProto(await mediator.Query(new GetDeploymentQuery(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id")), ctx.CancellationToken)), ctx.CancellationToken);

    public override async Task<Deployment> StopDeployment(GetDeploymentRequest r, ServerCallContext ctx)
    {
        var d = await mediator.Send(new StopDeploymentCommand(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id")), ctx.CancellationToken);
        if (d.JobId is { } job) await launcher.StopJobAsync(job, ctx.CancellationToken);
        return ToProto(d);
    }

    public override async Task<EndpointKey> CreateEndpointKey(CreateEndpointKeyRequest r, ServerCallContext ctx)
    {
        var (id, key, prefix) = await mediator.Send(new CreateEndpointKeyCommand(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id"), r.Name), ctx.CancellationToken);
        return new EndpointKey { Id = id.ToString(), Key = key, Prefix = prefix };
    }

    public override async Task<Deployment> StartDeployment(StartDeploymentRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var cur = await mediator.Query(new GetDeploymentQuery(ws, G(r.DeploymentId, "deployment_id")), ctx.CancellationToken);
        // A failed replica may still hold its container: stopped before the new one starts.
        if (cur.JobId is { } old && cur.State == "failed") await launcher.StopJobAsync(old, ctx.CancellationToken);
        return await WithUrl(ToProto(await mediator.Send(new StartDeploymentCommand(ws, cur.Id,
            string.IsNullOrEmpty(r.ModelVersionId) ? null : G(r.ModelVersionId, "model_version_id")), ctx.CancellationToken)), ctx.CancellationToken);
    }

    public override async Task<DeleteDeploymentResponse> DeleteDeployment(GetDeploymentRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var cur = await mediator.Query(new GetDeploymentQuery(ws, G(r.DeploymentId, "deployment_id")), ctx.CancellationToken);
        await mediator.Send(new DeleteDeploymentCommand(ws, cur.Id), ctx.CancellationToken);
        if (cur.JobId is { } old && cur.State == "failed") await launcher.StopJobAsync(old, ctx.CancellationToken);
        return new DeleteDeploymentResponse();
    }

    public override async Task<ListEndpointKeysResponse> ListEndpointKeys(GetDeploymentRequest r, ServerCallContext ctx)
    {
        var res = new ListEndpointKeysResponse();
        foreach (var k in await mediator.Query(new ListEndpointKeysQuery(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id")), ctx.CancellationToken))
            res.Keys.Add(ToProto(k));
        return res;
    }

    public override async Task<EndpointKeyInfo> RevokeEndpointKey(RevokeEndpointKeyRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var dep = G(r.DeploymentId, "deployment_id");
        var id = G(r.KeyId, "key_id");
        await mediator.Send(new RevokeEndpointKeyCommand(ws, dep, id), ctx.CancellationToken);
        return (await mediator.Query(new ListEndpointKeysQuery(ws, dep), ctx.CancellationToken)).Where(k => k.Id == id).Select(ToProto).FirstOrDefault()
               ?? throw new NotFoundException("SERVE_KEY_NOT_FOUND", "Key not found");
    }

    private static EndpointKeyInfo ToProto(EndpointKeyRow k) => new()
    {
        Id = k.Id.ToString(), Name = k.Name, Prefix = k.Prefix, CreatedAt = Timestamp.FromDateTimeOffset(k.CreatedAt),
        RevokedAt = k.RevokedAt is { } x ? Timestamp.FromDateTimeOffset(x) : null,
    };

    public override async Task<Deployment> StartTest(StartTestRequest r, ServerCallContext ctx) =>
        await WithUrl(ToProto(await mediator.Send(new StartTestDeploymentCommand(G(r.WorkspaceId, "workspace_id"), G(r.ModelVersionId, "model_version_id"),
            r.Gpus, string.IsNullOrEmpty(r.ImageOverride) ? null : r.ImageOverride), ctx.CancellationToken)), ctx.CancellationToken);

    public override async Task<TryPredictResponse> TryPredict(TryPredictRequest r, ServerCallContext ctx)
    {
        if (r.Inputs.Count is 0 or > 16) throw new ValidationException(new Dictionary<string, string> { ["inputs"] = "1–16 inputs per call" });
        var d = await mediator.Query(new ResolveTestTargetQuery(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id")), ctx.CancellationToken);
        var req = new PredictRequest();
        req.Inputs.AddRange(r.Inputs);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        PredictResponse res;
        try { res = await clients.For(d.Endpoint!).PredictAsync(req, deadline: TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(2), cancellationToken: ctx.CancellationToken); }
        catch (RpcException e) { throw new UnavailableException("SERVE_REPLICA_ERROR", string.IsNullOrEmpty(e.Status.Detail) ? e.Status.StatusCode.ToString() : e.Status.Detail); }
        var latency = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        await mediator.Send(new TouchDeploymentCommand(d.Id), ctx.CancellationToken);
        var resp = new TryPredictResponse { LatencyMs = Math.Round(latency, 1), Model = $"{d.ModelName}@v{d.ModelVersion}" };
        resp.Outputs.AddRange(res.Outputs);
        return resp;
    }

    private async Task<Deployment> WithUrl(Deployment d, CancellationToken ct)
    {
        d.PublicUrl = await routes.DeploymentUrlAsync(Guid.Parse(d.WorkspaceId), d.Slug, ct) ?? "";
        return d;
    }

    private static Deployment ToProto(DeploymentRow d) => new()
    {
        Id = d.Id.ToString(), WorkspaceId = d.WorkspaceId.ToString(), Slug = d.Slug, ModelVersionId = d.ModelVersionId.ToString(),
        ModelName = d.ModelName, ModelVersion = d.ModelVersion, Task = d.Task, State = d.State, StateReason = d.StateReason ?? "",
        JobId = d.JobId?.ToString() ?? "", PredictUrl = $"/v1/deployments/{d.WorkspaceId}/{d.Slug}/predict",
        CreatedAt = Timestamp.FromDateTimeOffset(d.CreatedAt), Purpose = d.Purpose, IdleStopMin = d.IdleStopMin ?? 0,
        LastUsedAt = d.LastUsedAt is { } u ? Timestamp.FromDateTimeOffset(u) : null, Gpus = d.Gpus ?? -1,
    };
}
