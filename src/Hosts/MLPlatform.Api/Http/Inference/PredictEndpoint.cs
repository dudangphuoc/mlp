using System.Text.Json.Nodes;
using Google.Protobuf;
using MLPlatform.ControlPlane.Application.Features;
using MLPlatform.ControlPlane.Application.LlmOps;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Http.Inference;

/// <summary>POST /v1/deployments/{ws}/{slug}/predict: chat for LLM deployments, otherwise task inputs (tabular, text,
/// image, or an entity whose online features are filled in).</summary>
public static class PredictEndpoint
{
    public static void MapGateway(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/deployments/{ws:guid}/{slug}/predict", HandleAsync);

    private static async Task<IResult> HandleAsync(Guid ws, string slug, HttpContext http, IMediator mediator, ModelServerClients clients) =>
        await EndpointKeys.ResolveAsync(ws, slug, http, mediator, requireHealthy: true) switch
        {
            DeploymentRow deployment => await PredictAsync(deployment, http, mediator, clients),
            IResult refused => refused,
            null => PredictionJson.Error(404, "SERVE_NOT_FOUND", "Deployment not found"),
        };

    private static async Task<IResult> PredictAsync(DeploymentRow deployment, HttpContext http, IMediator mediator, ModelServerClients clients)
    {
        if (await EndpointKeys.ReadBodyAsync(http) is not { } body) return PredictionJson.Error(400, "VALIDATION_FAILED", "Body is not JSON");
        if (body["messages"] is JsonArray messages) return await ChatAsync(deployment, body, messages, http, mediator);
        var items = (body["inputs"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var online = await OnlineFeaturesAsync(deployment, items, http, mediator);
        if (online.Error is { } featureError) return featureError;
        var request = new PredictRequest();
        foreach (var item in items)
        {
            if (TaskInputs.From(WithOnlineFeatures(item, online.Values)) is not { } input)
                return PredictionJson.Error(400, "VALIDATION_FAILED", "Each input needs tabular | text | image_b64");
            request.Inputs.Add(input);
        }
        if (request.Inputs.Count == 0) return PredictionJson.Error(400, "VALIDATION_FAILED", "No inputs");
        return await CallReplicaAsync(deployment, request, http, mediator, clients);
    }

    private static async Task<IResult> ChatAsync(DeploymentRow deployment, JsonNode body, JsonArray messages, HttpContext http, IMediator mediator)
    {
        var list = messages.OfType<JsonObject>().Select(m => (m["role"]?.ToString() ?? "user", m["content"]?.ToString() ?? "")).ToList();
        if (list.Count == 0) return PredictionJson.Error(400, "VALIDATION_FAILED", "messages is empty");
        LlmCallResult reply;
        try
        {
            reply = await mediator.Send(new GatewayChatCommand(deployment, list,
                body["max_tokens"]?.GetValue<int>() ?? 512, body["temperature"]?.GetValue<double>() ?? 0.7), http.RequestAborted);
        }
        catch (AppException e) { return PredictionJson.Error(502, e.Reason, e.Message); }
        if (reply.Blocked) return PredictionJson.Error(400, "LLM_GUARDRAIL_BLOCKED", reply.Reason ?? "Blocked by guardrail");
        http.Response.Headers["X-Mlp-Model-Version"] = $"{deployment.ModelName}@v{deployment.ModelVersion}";
        return PredictionJson.Ok(new JsonObject
        {
            ["output"] = new JsonObject { ["text"] = reply.Text },
            ["usage"] = new JsonObject { ["prompt_tokens"] = reply.PromptTokens, ["completion_tokens"] = reply.CompletionTokens, ["latency_ms"] = Math.Round(reply.LatencyMs, 1) },
            ["flags"] = new JsonArray(reply.Flags.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
        });
    }

    private static async Task<(Dictionary<string, OnlineValue>? Values, IResult? Error)> OnlineFeaturesAsync(
        DeploymentRow deployment, List<JsonObject> items, HttpContext http, IMediator mediator)
    {
        var keys = items.Where(i => i["entity"] is JsonValue).Select(i => i["entity"]!.ToString()).Distinct().ToList();
        if (keys.Count == 0) return (null, null);
        IReadOnlyList<OnlineValue>? values;
        try { values = await mediator.Query(new ResolveServingFeaturesQuery(deployment, keys), http.RequestAborted); }
        catch (AppException e) { return (null, PredictionJson.Error(400, e.Reason, e.Message)); }
        if (values is null) return (null, PredictionJson.Error(400, "FEATURE_NOT_BOUND", "Deployment has no features bound; send tabular or bind a feature view"));
        var missing = values.Where(v => v.Missing.Count > 0).Select(v => $"{v.Key}: {string.Join(", ", v.Missing)}").ToList();
        if (missing.Count > 0) return (null, PredictionJson.Error(422, "FEATURE_MISSING", "Missing features (not available or past TTL): " + string.Join("; ", missing)));
        return (values.ToDictionary(v => v.Key), null);
    }

    private static JsonObject WithOnlineFeatures(JsonObject item, Dictionary<string, OnlineValue>? online)
    {
        if (item["entity"] is not JsonValue entity || online is null) return item;
        var merged = new JsonObject();
        foreach (var (k, v) in online[entity.ToString()].Values) merged[k] = v?.DeepClone();
        foreach (var (k, v) in item["tabular"] as JsonObject ?? []) merged[k] = v?.DeepClone();
        return new JsonObject { ["tabular"] = merged };
    }

    private static async Task<IResult> CallReplicaAsync(DeploymentRow deployment, PredictRequest request, HttpContext http, IMediator mediator, ModelServerClients clients)
    {
        PredictResponse response;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { response = await clients.For(deployment.Endpoint!).PredictAsync(request, cancellationToken: http.RequestAborted); }
        catch (global::Grpc.Core.RpcException e) { return PredictionJson.Error(502, "SERVE_REPLICA_ERROR", e.Status.Detail); }
        var latency = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var logged = await mediator.Send(new LogPredictionsCommand(deployment.Id, deployment.ModelVersion, latency,
            request.Inputs.Zip(response.Outputs, (i, o) => (PredictionJson.Features(i).ToJsonString(), PredictionJson.Summary(o).ToJsonString())).ToList()), http.RequestAborted);
        http.Response.Headers["X-Mlp-Prediction-Ids"] = string.Join(',', logged);
        http.Response.Headers["X-Mlp-Model-Version"] = $"{deployment.ModelName}@v{deployment.ModelVersion}";
        return PredictionJson.Ok(new JsonObject { ["predictions"] = new JsonArray([.. response.Outputs.Select(PredictionJson.ToJson)]) });
    }
}
