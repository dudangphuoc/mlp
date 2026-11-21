using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Http.Inference;

/// <summary>POST /v1/deployments/{ws}/{slug}/feedback: late ground truth,
/// {"feedback":[{"prediction_id","label"|"value"}]}.</summary>
public static class FeedbackEndpoint
{
    private const int MaxItems = 1000;

    public static void MapFeedback(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/deployments/{ws:guid}/{slug}/feedback", HandleAsync);

    private static async Task<IResult> HandleAsync(Guid ws, string slug, HttpContext http, IMediator mediator) =>
        await EndpointKeys.ResolveAsync(ws, slug, http, mediator, requireHealthy: false) switch
        {
            DeploymentRow deployment => await RecordAsync(deployment, http, mediator),
            IResult refused => refused,
            null => PredictionJson.Error(404, "SERVE_NOT_FOUND", "Deployment not found"),
        };

    private static async Task<IResult> RecordAsync(DeploymentRow deployment, HttpContext http, IMediator mediator)
    {
        if (await EndpointKeys.ReadBodyAsync(http) is not { } body) return PredictionJson.Error(400, "VALIDATION_FAILED", "Body is not JSON");
        var items = new List<(Guid, string)>();
        foreach (var f in (body["feedback"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            if (!Guid.TryParse(f["prediction_id"]?.ToString(), out var id)) return PredictionJson.Error(400, "VALIDATION_FAILED", "Missing prediction_id");
            if (GroundTruth(f) is not { } truth) return PredictionJson.Error(400, "VALIDATION_FAILED", "Each item needs a label or value");
            items.Add((id, truth.ToJsonString()));
        }
        if (items.Count is 0 or > MaxItems) return PredictionJson.Error(400, "VALIDATION_FAILED", "1–1000 feedback items required");
        var accepted = await mediator.Send(new RecordFeedbackCommand(deployment.Id, items), http.RequestAborted);
        return PredictionJson.Ok(new JsonObject { ["accepted"] = accepted });
    }

    private static JsonObject? GroundTruth(JsonObject item) =>
        item["label"] is { } label ? new JsonObject { ["label"] = label.ToString() }
        : item["value"] is JsonValue v && v.TryGetValue<double>(out var value) ? new JsonObject { ["value"] = value }
        : null;
}
