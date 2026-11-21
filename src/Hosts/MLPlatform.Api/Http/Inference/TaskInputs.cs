using System.Text.Json.Nodes;
using Google.Protobuf;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Api.Http.Inference;

/// <summary>Maps one JSON input of the predict API to the model server's task input; null when its shape is
/// unknown.</summary>
public static class TaskInputs
{
    private const float DefaultConfidence = 0.25f;

    public static TaskInput? From(JsonObject item)
    {
        if (item["tabular"] is JsonObject row) return new TaskInput { Tabular = Tabular(row) };
        if (item["text"] is JsonValue text)
        {
            var input = new TextInput { Text = text.ToString() };
            input.Labels.AddRange(Labels(item));
            return new TaskInput { Text = input };
        }
        if (item["image_b64"] is JsonValue image)
        {
            var input = new ImageInput { Data = ByteString.FromBase64(image.ToString()), Conf = (float?)item["conf"]?.GetValue<double>() ?? DefaultConfidence };
            input.Labels.AddRange(Labels(item));
            return new TaskInput { Image = input };
        }
        return null;
    }

    private static TabularInput Tabular(JsonObject row)
    {
        var input = new TabularInput();
        foreach (var (k, v) in row)
            if (v is JsonValue jv && jv.TryGetValue<double>(out var d)) input.Numbers[k] = d; else input.Strings[k] = v?.ToString() ?? "";
        return input;
    }

    private static IEnumerable<string> Labels(JsonNode item) =>
        item["labels"] is JsonArray a ? a.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0) : [];
}
