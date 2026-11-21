using MLPlatform.ControlPlane.Infrastructure;
using System.Text.Json.Nodes;
using Google.Protobuf;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Http.Inference;

/// <summary>
/// Public REST inference gateway (M12 §5 contract; JSON is an allowed exception, 20/03 §1):
/// POST /v1/deployments/{ws}/{slug}/predict, header `Authorization: Bearer mlpd_…`.
/// Body: {"inputs":[{"tabular":{"a":1}} | {"text":"…"} | {"image_b64":"…","conf":0.25}]} → {"predictions":[…]} theo task.
/// </summary>
public static class Gateway
{
    /// <summary>JSON returned to clients keeps non-ASCII text as is (no \uXXXX escaping).</summary>
    private static readonly System.Text.Json.JsonSerializerOptions Out = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void MapGateway(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/deployments/{ws:guid}/{slug}/predict", async (Guid ws, string slug, HttpContext http, IMediator mediator, ModelServerClients clients) =>
        {
            var auth = http.Request.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer mlpd_", StringComparison.Ordinal)) return Error(401, "SERVE_KEY_INVALID", "Missing endpoint key");
            DeploymentRow dep;
            try { dep = await mediator.Query(new ResolveEndpointQuery(auth[7..], ws, slug), http.RequestAborted); }
            catch (UnauthenticatedException e) { return Error(401, e.Reason, e.Message); }
            catch (NotFoundException e) { return Error(404, e.Reason, e.Message); }
            catch (UnavailableException e) { return Error(503, e.Reason, e.Message); }

            JsonNode? body;
            try { body = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted); }
            catch (System.Text.Json.JsonException) { return Error(400, "VALIDATION_FAILED", "Body is not JSON"); }
            // M15: {"messages":[{role,content}], "max_tokens", "temperature"} → chat with guardrails + token counting.
            if (body?["messages"] is JsonArray msgs)
            {
                var list = msgs.OfType<JsonObject>().Select(m => (m["role"]?.ToString() ?? "user", m["content"]?.ToString() ?? "")).ToList();
                if (list.Count == 0) return Error(400, "VALIDATION_FAILED", "messages is empty");
                MLPlatform.ControlPlane.Application.LlmOps.LlmCallResult r;
                try
                {
                    r = await mediator.Send(new MLPlatform.ControlPlane.Application.LlmOps.GatewayChatCommand(dep, list,
                        body["max_tokens"]?.GetValue<int>() ?? 512, body["temperature"]?.GetValue<double>() ?? 0.7), http.RequestAborted);
                }
                catch (AppException e) { return Error(502, e.Reason, e.Message); }
                if (r.Blocked) return Error(400, "LLM_GUARDRAIL_BLOCKED", r.Reason ?? "Blocked by guardrail");
                http.Response.Headers["X-Mlp-Model-Version"] = $"{dep.ModelName}@v{dep.ModelVersion}";
                return Results.Text(new JsonObject
                {
                    ["output"] = new JsonObject { ["text"] = r.Text },
                    ["usage"] = new JsonObject { ["prompt_tokens"] = r.PromptTokens, ["completion_tokens"] = r.CompletionTokens, ["latency_ms"] = Math.Round(r.LatencyMs, 1) },
                    ["flags"] = new JsonArray(r.Flags.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
                }.ToJsonString(Out), "application/json");
            }
            var req = new PredictRequest();
            // M07: input {"entity":"c1", "tabular"?:{overrides}} → fetch online features via the deployment's binding (same definition as at training time).
            var items = (body?["inputs"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
            var entityKeys = items.Where(i => i["entity"] is JsonValue).Select(i => i["entity"]!.ToString()).Distinct().ToList();
            Dictionary<string, MLPlatform.ControlPlane.Application.Features.OnlineValue>? online = null;
            if (entityKeys.Count > 0)
            {
                IReadOnlyList<MLPlatform.ControlPlane.Application.Features.OnlineValue>? vals;
                try { vals = await mediator.Query(new MLPlatform.ControlPlane.Application.Features.ResolveServingFeaturesQuery(dep, entityKeys), http.RequestAborted); }
                catch (AppException e) { return Error(400, e.Reason, e.Message); }
                if (vals is null) return Error(400, "FEATURE_NOT_BOUND", "Deployment has no features bound; send tabular or bind a feature view");
                var missing = vals.Where(v => v.Missing.Count > 0).Select(v => $"{v.Key}: {string.Join(", ", v.Missing)}").ToList();
                if (missing.Count > 0) return Error(422, "FEATURE_MISSING", "Missing features (not available or past TTL): " + string.Join("; ", missing));
                online = vals.ToDictionary(v => v.Key);
            }
            foreach (var it in items)
            {
                var i = it;
                if (i["entity"] is JsonValue ek && online is not null)
                {
                    var merged = new JsonObject();
                    foreach (var (k, v) in online[ek.ToString()].Values) merged[k] = v?.DeepClone();
                    foreach (var (k, v) in i["tabular"] as JsonObject ?? []) merged[k] = v?.DeepClone();
                    i = new JsonObject { ["tabular"] = merged };
                }
                if (i["tabular"] is JsonObject row)
                {
                    var t = new TabularInput();
                    foreach (var (k, v) in row)
                        if (v is JsonValue jv && jv.TryGetValue<double>(out var d)) t.Numbers[k] = d; else t.Strings[k] = v?.ToString() ?? "";
                    req.Inputs.Add(new TaskInput { Tabular = t });
                }
                else if (i["text"] is JsonValue tx)
                {
                    var ti = new TextInput { Text = tx.ToString() };
                    ti.Labels.AddRange(Labels(i));   // zero-shot classification: candidate labels
                    req.Inputs.Add(new TaskInput { Text = ti });
                }
                else if (i["image_b64"] is JsonValue img)
                {
                    var ii = new ImageInput { Data = ByteString.FromBase64(img.ToString()), Conf = (float?)i["conf"]?.GetValue<double>() ?? 0.25f };
                    ii.Labels.AddRange(Labels(i));   // open-vocabulary detection: what to look for
                    req.Inputs.Add(new TaskInput { Image = ii });
                }
                else return Error(400, "VALIDATION_FAILED", "Each input needs tabular | text | image_b64");
            }
            if (req.Inputs.Count == 0) return Error(400, "VALIDATION_FAILED", "No inputs");
            PredictResponse res;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { res = await clients.For(dep.Endpoint!).PredictAsync(req, cancellationToken: http.RequestAborted); }
            catch (global::Grpc.Core.RpcException e) { return Error(502, "SERVE_REPLICA_ERROR", e.Status.Detail); }
            var latency = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var preds = new JsonArray();
            foreach (var o in res.Outputs) preds.Add(ToJson(o));
            // M13: prediction log (summary features + result) for drift/quality; the id is returned in a header so ground truth can be sent later.
            var logged = await mediator.Send(new MLPlatform.ControlPlane.Application.Monitoring.LogPredictionsCommand(dep.Id, dep.ModelVersion, latency,
                req.Inputs.Zip(res.Outputs, (i, o) => (Features(i).ToJsonString(), Summary(o).ToJsonString())).ToList()), http.RequestAborted);
            http.Response.Headers["X-Mlp-Prediction-Ids"] = string.Join(',', logged);
            http.Response.Headers["X-Mlp-Model-Version"] = $"{dep.ModelName}@v{dep.ModelVersion}";
            return Results.Text(new JsonObject { ["predictions"] = preds }.ToJsonString(Out), "application/json");
        });

    /// <summary>`labels`: ["fire","smoke"] for zero-shot models.</summary>
    private static IEnumerable<string> Labels(JsonNode i) =>
        i["labels"] is JsonArray a ? a.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0) : [];

    /// <summary>
    /// POST /v1/deployments/{ws}/{slug}/feedback: late ground-truth labels (M13): {"feedback":[{"prediction_id":"…","label":"1"} | {"prediction_id":"…","value":1.5}]}.
    /// </summary>
    public static void MapFeedback(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/deployments/{ws:guid}/{slug}/feedback", async (Guid ws, string slug, HttpContext http, IMediator mediator) =>
        {
            var auth = http.Request.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer mlpd_", StringComparison.Ordinal)) return Error(401, "SERVE_KEY_INVALID", "Missing endpoint key");
            DeploymentRow dep;
            try { dep = await mediator.Query(new ResolveEndpointQuery(auth[7..], ws, slug, RequireHealthy: false), http.RequestAborted); }
            catch (UnauthenticatedException e) { return Error(401, e.Reason, e.Message); }
            catch (NotFoundException e) { return Error(404, e.Reason, e.Message); }
            JsonNode? body;
            try { body = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted); }
            catch (System.Text.Json.JsonException) { return Error(400, "VALIDATION_FAILED", "Body is not JSON"); }
            var items = new List<(Guid, string)>();
            foreach (var f in (body?["feedback"] as JsonArray)?.OfType<JsonObject>() ?? [])
            {
                if (!Guid.TryParse(f["prediction_id"]?.ToString(), out var id)) return Error(400, "VALIDATION_FAILED", "Missing prediction_id");
                JsonObject gt = f["label"] is { } l ? new JsonObject { ["label"] = l.ToString() }
                    : f["value"] is JsonValue v && v.TryGetValue<double>(out var d) ? new JsonObject { ["value"] = d }
                    : null!;
                if (gt is null) return Error(400, "VALIDATION_FAILED", "Each item needs a label or value");
                items.Add((id, gt.ToJsonString()));
            }
            if (items.Count is 0 or > 1000) return Error(400, "VALIDATION_FAILED", "1–1000 feedback items required");
            var n = await mediator.Send(new MLPlatform.ControlPlane.Application.Monitoring.RecordFeedbackCommand(dep.Id, items), http.RequestAborted);
            return Results.Text(new JsonObject { ["accepted"] = n }.ToJsonString(Out), "application/json");
        });

    /// <summary>Logged features: table → column values; text/image → summary only (content not stored, 20/06 sensitive data).</summary>
    private static JsonObject Features(TaskInput i) => i.KindCase switch
    {
        TaskInput.KindOneofCase.Tabular => new JsonObject(i.Tabular.Numbers.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value)))
            .Concat(i.Tabular.Strings.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value))))),
        TaskInput.KindOneofCase.Text => new JsonObject { ["text_length"] = i.Text.Text.Length },
        TaskInput.KindOneofCase.Image => new JsonObject { ["image_bytes"] = i.Image.Data.Length },
        _ => new JsonObject(),
    };

    private static JsonObject Summary(TaskOutput o) => o.KindCase switch
    {
        TaskOutput.KindOneofCase.Tabular when o.Tabular.HasValue => new JsonObject { ["value"] = o.Tabular.Value },
        TaskOutput.KindOneofCase.Tabular => new JsonObject
            { ["label"] = o.Tabular.Label, ["score"] = o.Tabular.Probabilities.Count > 0 ? o.Tabular.Probabilities.Values.Max() : 0 },
        TaskOutput.KindOneofCase.Classify when o.Classify.Labels.Count > 0 => new JsonObject
            { ["label"] = o.Classify.Labels[0].Label, ["score"] = o.Classify.Labels[0].Score },
        TaskOutput.KindOneofCase.Detect => new JsonObject
            { ["objects"] = o.Detect.Objects.Count, ["score"] = o.Detect.Objects.Count > 0 ? o.Detect.Objects.Max(x => x.Score) : 0 },
        _ => new JsonObject(),
    };

    private static JsonNode ToJson(TaskOutput o) => o.KindCase switch
    {
        TaskOutput.KindOneofCase.Tabular => o.Tabular.HasValue
            ? new JsonObject { ["value"] = o.Tabular.Value }
            : new JsonObject { ["label"] = o.Tabular.Label, ["probabilities"] = new JsonObject(o.Tabular.Probabilities.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value)))) },
        TaskOutput.KindOneofCase.Classify => new JsonArray(o.Classify.Labels.Select(l => (JsonNode)new JsonObject { ["label"] = l.Label, ["score"] = l.Score }).ToArray()),
        TaskOutput.KindOneofCase.Detect => new JsonObject
        {
            ["imageSize"] = new JsonObject { ["width"] = o.Detect.Width, ["height"] = o.Detect.Height },
            ["objects"] = new JsonArray(o.Detect.Objects.Select(x => (JsonNode)new JsonObject
            {
                ["class"] = x.ClassName, ["classId"] = x.ClassId, ["confidence"] = x.Score,
                ["box"] = new JsonObject { ["x1"] = x.X1, ["y1"] = x.Y1, ["x2"] = x.X2, ["y2"] = x.Y2 },
            }).ToArray()),
        },
        TaskOutput.KindOneofCase.Ner => new JsonArray(o.Ner.Entities.Select(e => (JsonNode)new JsonObject
            { ["entity"] = e.Label, ["start"] = e.Start, ["end"] = e.End, ["text"] = e.Text, ["score"] = e.Score }).ToArray()),
        TaskOutput.KindOneofCase.Generate => new JsonObject { ["text"] = o.Generate.Text },
        TaskOutput.KindOneofCase.Asr => new JsonObject { ["text"] = o.Asr.Text },
        _ => new JsonObject(),
    };

    private static IResult Error(int status, string reason, string message) =>
        Results.Text(new JsonObject { ["error"] = new JsonObject { ["reason"] = reason, ["message"] = message } }.ToJsonString(Out), "application/json", statusCode: status);
}
