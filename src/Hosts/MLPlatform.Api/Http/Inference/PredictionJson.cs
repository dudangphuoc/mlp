using System.Text.Json.Nodes;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Api.Http.Inference;

/// <summary>JSON shapes of the public inference API: responses, logged features and prediction summaries.</summary>
public static class PredictionJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Out = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static IResult Ok(JsonObject body) => Results.Text(body.ToJsonString(Out), "application/json");

    public static IResult Error(int status, string reason, string message) =>
        Results.Text(new JsonObject { ["error"] = new JsonObject { ["reason"] = reason, ["message"] = message } }.ToJsonString(Out), "application/json", statusCode: status);

    /// <summary>Logged features: table columns as values; text and images only as sizes (content is never
    /// stored).</summary>
    public static JsonObject Features(TaskInput i) => i.KindCase switch
    {
        TaskInput.KindOneofCase.Tabular => new JsonObject(i.Tabular.Numbers.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value)))
            .Concat(i.Tabular.Strings.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value))))),
        TaskInput.KindOneofCase.Text => new JsonObject { ["text_length"] = i.Text.Text.Length },
        TaskInput.KindOneofCase.Image => new JsonObject { ["image_bytes"] = i.Image.Data.Length },
        _ => new JsonObject(),
    };

    public static JsonObject Summary(TaskOutput o) => o.KindCase switch
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

    public static JsonNode ToJson(TaskOutput o) => o.KindCase switch
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
}
