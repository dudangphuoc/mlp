using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tuning;

/// <summary>
/// Search space (M10 §2): each parameter is `{"values":[…]}` (discrete; grid enumerates all) or `{"min":a,"max":b,"log":bool,"int":bool}` (continuous; random only).
/// Parameters are generated deterministically from (seed, trial number) → rerunning a sweep with the same seed yields the same trial sequence.
/// </summary>
public sealed class SearchSpace
{
    private sealed record Dim(string Name, JsonNode?[]? Values, double Min, double Max, bool Log, bool Int);
    private readonly List<Dim> _dims;

    private SearchSpace(List<Dim> dims) => _dims = dims;

    public static SearchSpace Parse(string json, Func<string, bool> knownParam)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw Bad("Search space must be an object"); }
        catch (JsonException) { throw Bad("Invalid JSON"); }
        if (root.Count == 0) throw Bad("At least one parameter is required");
        var dims = new List<Dim>();
        foreach (var (name, node) in root)
        {
            if (!knownParam(name)) throw Bad($"Parameter not in the recipe: {name}");
            if (node is not JsonObject o) throw Bad($"{name}: requires {{values}} or {{min,max}}");
            if (o["values"] is JsonArray arr)
            {
                if (arr.Count == 0) throw Bad($"{name}: values is empty");
                dims.Add(new Dim(name, arr.Select(v => v?.DeepClone()).ToArray(), 0, 0, false, false));
            }
            else
            {
                double Num(string k) => o[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : throw Bad($"{name}: missing {k}");
                var (min, max) = (Num("min"), Num("max"));
                var log = o["log"]?.GetValue<bool>() == true;
                if (max <= min || (log && min <= 0)) throw Bad($"{name}: invalid range");
                dims.Add(new Dim(name, null, min, max, log, o["int"]?.GetValue<bool>() == true));
            }
        }
        return new SearchSpace(dims);
    }

    private static ValidationException Bad(string msg) => new(new Dictionary<string, string> { ["space"] = msg });

    /// <summary>Number of grid combinations; null when there is a continuous dimension (grid not applicable).</summary>
    public long? GridSize => _dims.All(d => d.Values is not null) ? _dims.Aggregate(1L, (n, d) => n * d.Values!.Length) : null;

    public JsonObject Grid(int index)
    {
        var o = new JsonObject();
        foreach (var d in Enumerable.Reverse(_dims))
        {
            o[d.Name] = d.Values![index % d.Values.Length]?.DeepClone();
            index /= d.Values.Length;
        }
        return Order(o);
    }

    public JsonObject Random(int seed, int number)
    {
        var rng = new Random(unchecked(seed * 7919 + number));
        var o = new JsonObject();
        foreach (var d in _dims)
        {
            if (d.Values is not null) { o[d.Name] = d.Values[rng.Next(d.Values.Length)]?.DeepClone(); continue; }
            var u = rng.NextDouble();
            var x = d.Log ? Math.Exp(Math.Log(d.Min) + u * (Math.Log(d.Max) - Math.Log(d.Min))) : d.Min + u * (d.Max - d.Min);
            o[d.Name] = d.Int ? JsonValue.Create((long)Math.Round(x)) : JsonValue.Create(Math.Round(x, 6));
        }
        return o;
    }

    private JsonObject Order(JsonObject o)
    {
        var r = new JsonObject();
        foreach (var d in _dims) r[d.Name] = o[d.Name]?.DeepClone();
        return r;
    }
}
