using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

public static class Evaluations
{
    public const int MaxSamplesLimit = 5000;

    /// <summary>Class names by id from a label schema (lenient: gaps keep their numeric id).</summary>
    public static List<string> Classes(string? classesJson)
    {
        var map = new SortedDictionary<int, string>();
        foreach (var n in (classesJson is null ? null : JsonNode.Parse(classesJson) as JsonArray) ?? [])
            if (n?["id"] is JsonValue id && id.TryGetValue<int>(out var i)) map[i] = n["name"]?.ToString() ?? i.ToString(CultureInfo.InvariantCulture);
        if (map.Count == 0) return [];
        var list = new List<string>();
        for (var k = 0; k <= map.Keys.Max(); k++) list.Add(map.TryGetValue(k, out var v) ? v : k.ToString(CultureInfo.InvariantCulture));
        return list;
    }

}
