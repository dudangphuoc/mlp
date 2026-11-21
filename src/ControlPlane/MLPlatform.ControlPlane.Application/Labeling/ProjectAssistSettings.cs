using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Effective settings (defaults when never saved).</summary>
public sealed record ProjectAssistSettings(List<(string Kind, Guid Id)> Sources, Dictionary<string, int> ClassMapping, bool AllowExternal, double MinConfidence, bool Saved)
{
    public static ProjectAssistSettings From(AssistSettingsRow? r)
    {
        if (r is null) return new([], [], false, 0.3, false);
        var sources = new List<(string, Guid)>();
        try
        {
            foreach (var n in JsonNode.Parse(r.SourcesJson) as JsonArray ?? [])
                if (n?["kind"]?.ToString() is { } k && Guid.TryParse(n["id"]?.ToString(), out var id)) sources.Add((k, id));
        }
        catch (System.Text.Json.JsonException) { }
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (JsonNode.Parse(r.ClassMappingJson) is JsonObject o)
                foreach (var (k, v) in o) if (v is JsonValue jv && jv.TryGetValue<int>(out var cid)) map[k] = cid;
        }
        catch (System.Text.Json.JsonException) { }
        return new(sources, map, r.AllowExternal, r.MinConfidence, true);
    }

    /// <summary>AI assist is on by default: a project that never saved its settings uses every internal source (data
    /// stays on the platform).</summary>
    public bool IsEnabled(string kind, Guid id, bool external) => Saved ? Sources.Contains((kind, id)) : !external;
}
