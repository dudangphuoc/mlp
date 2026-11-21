using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Checks training parameters against the recipe's schema (type, minimum, maximum, multipleOf, enum) before
/// anything is queued.</summary>
public static class TrainParams
{
    public static Dictionary<string, string> Validate(JsonObject values, JsonObject schema)
    {
        var errors = new Dictionary<string, string>();
        foreach (var (key, node) in values)
        {
            if (schema[key] is not JsonObject def) continue;
            if (Check(key, node, def) is { } e) errors["params." + key] = e;
        }
        return errors;
    }

    private static string? Check(string key, JsonNode? node, JsonObject def)
    {
        var type = def["type"] is JsonValue tv ? tv.ToString() : null;
        if (node is not JsonValue v) return $"Parameter {key}: a single value is expected";
        if (def["enum"] is JsonArray options)
        {
            var text = v.ToString();
            return options.Any(o => o?.ToString() == text) ? null : $"Parameter {key}: must be one of {string.Join(", ", options.Select(o => o?.ToString()))}";
        }
        switch (type)
        {
            case "boolean":
                return v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? null : $"Parameter {key}: must be true or false";
            case "string":
                return v.GetValueKind() == JsonValueKind.String ? null : $"Parameter {key}: must be text";
            case "integer" or "number":
                if (v.GetValueKind() != JsonValueKind.Number || !v.TryGetValue<double>(out var n) || !double.IsFinite(n)) return $"Parameter {key}: must be a number";
                if (type == "integer" && n != Math.Floor(n)) return $"Parameter {key}: must be a whole number";
                if (Num(def, "minimum") is { } lo && n < lo) return $"Parameter {key}: must be at least {Text(lo)}";
                if (Num(def, "exclusiveMinimum") is { } xlo && n <= xlo) return $"Parameter {key}: must be greater than {Text(xlo)}";
                if (Num(def, "maximum") is { } hi && n > hi) return $"Parameter {key}: must be at most {Text(hi)}";
                if (Num(def, "multipleOf") is { } m && m > 0 && Math.Abs(n / m - Math.Round(n / m)) > 1e-9) return $"Parameter {key}: must be a multiple of {Text(m)}";
                return null;
            default:
                return null;
        }
    }

    private static double? Num(JsonObject def, string name) => def[name] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
    private static string Text(double d) => d.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>`data.input` is a format name, or a map task → format; "none" (or absent) means the recipe brings no
    /// training data.</summary>
    public static bool NeedsData(JsonObject manifest, string task)
    {
        var input = manifest["data"]?["input"];
        var format = input is JsonObject byTask ? byTask[task]?.ToString() : input?.ToString();
        return !string.IsNullOrEmpty(format) && format != "none";
    }
}
