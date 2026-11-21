using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Pipelines;

/// <summary>Pipeline definition (M14 §2): `{"params":{…defaults},
/// "steps":[{"id","kind","needs":[…],"config":{…},"retries":0}]}`.</summary>
public static partial class PipelineDef
{
    public sealed record Step(string Id, string Kind, string[] Needs, JsonObject Config, int Retries);

    public static readonly string[] Kinds = ["dataset", "train", "job", "gate", "register", "promote"];

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,40}$")]
    private static partial Regex StepId();

    [GeneratedRegex(@"\$\{(params|steps)\.([A-Za-z0-9_-]+)(?:\.([A-Za-z0-9_]+))?\}")]
    private static partial Regex Ref();

    private static ValidationException Bad(string msg) => new(new Dictionary<string, string> { ["definition"] = msg });

    public static (JsonObject Params, List<Step> Steps) Parse(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw Bad("Definition must be an object"); }
        catch (JsonException) { throw Bad("Invalid JSON"); }
        var steps = new List<Step>();
        foreach (var n in root["steps"] as JsonArray ?? throw Bad("steps is required"))
        {
            if (n is not JsonObject o) throw Bad("Each step must be an object");
            var id = o["id"]?.ToString() ?? "";
            if (!StepId().IsMatch(id)) throw Bad($"Invalid step id: '{id}'");
            if (steps.Any(s => s.Id == id)) throw Bad($"Duplicate step id {id}");
            var kind = o["kind"]?.ToString() ?? "";
            if (!Kinds.Contains(kind)) throw Bad($"Step {id}: kind '{kind}' is not supported ({string.Join(", ", Kinds)})");
            var needs = (o["needs"] as JsonArray)?.Select(x => x?.ToString() ?? "").ToArray() ?? [];
            var config = o["config"] as JsonObject ?? [];
            Require(id, kind, config);
            steps.Add(new Step(id, kind, needs, (JsonObject)config.DeepClone(), Math.Clamp(o["retries"]?.GetValue<int>() ?? 0, 0, 5)));
        }
        if (steps.Count == 0) throw Bad("At least one step is required");
        foreach (var s in steps)
            foreach (var need in s.Needs)
                if (steps.All(x => x.Id != need)) throw Bad($"Step {s.Id} needs '{need}', which does not exist");
        var indeg = steps.ToDictionary(s => s.Id, s => s.Needs.Length);
        var queue = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var seen = 0;
        while (queue.Count > 0)
        {
            var id = queue.Dequeue(); seen++;
            foreach (var s in steps.Where(s => s.Needs.Contains(id)))
                if (--indeg[s.Id] == 0) queue.Enqueue(s.Id);
        }
        if (seen != steps.Count) throw Bad("The graph has a cycle");
        foreach (var s in steps)
            foreach (Match m in Ref().Matches(s.Config.ToJsonString()))
                if (m.Groups[1].Value == "steps" && !Upstream(steps, s).Contains(m.Groups[2].Value))
                    throw Bad($"Step {s.Id} references ${{steps.{m.Groups[2].Value}}} but does not depend on that step");
        return ((root["params"] as JsonObject)?.DeepClone() as JsonObject ?? [], steps);
    }

    private static HashSet<string> Upstream(List<Step> steps, Step s)
    {
        var set = new HashSet<string>();
        var stack = new Stack<string>(s.Needs);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!set.Add(id)) continue;
            foreach (var n in steps.First(x => x.Id == id).Needs) stack.Push(n);
        }
        return set;
    }

    private static void Require(string id, string kind, JsonObject c)
    {
        string[] need = kind switch
        {
            "dataset" => ["repo", "name", "task"],
            "train" => ["recipe", "variant"],
            "job" => ["image", "command"],
            "gate" => ["left", "op", "right"],
            "register" => ["run_id", "model_name"],
            "promote" => ["version_id", "stage"],
            _ => [],
        };
        var missing = need.Where(k => c[k] is null).ToList();
        if (missing.Count > 0) throw Bad($"Step {id} ({kind}) is missing config: {string.Join(", ", missing)}");
        if (kind == "train" && c["project_id"] is null && c["project_name"] is null) throw Bad($"Step {id} (train) needs project_id or project_name");
        if (kind == "gate" && c["op"]?.ToString() is not (">" or ">=" or "<" or "<=" or "==" or "!="))
            throw Bad($"Step {id}: op must be one of > >= < <= == !=");
    }

    /// <summary>Replaces references in config with parameter values / outputs of earlier steps.</summary>
    public static JsonNode? Resolve(JsonNode? node, JsonObject runParams, IReadOnlyDictionary<string, JsonObject> outputs)
    {
        switch (node)
        {
            case JsonObject o:
                var r = new JsonObject();
                foreach (var (k, v) in o) r[k] = Resolve(v, runParams, outputs);
                return r;
            case JsonArray a:
                return new JsonArray(a.Select(x => Resolve(x, runParams, outputs)).ToArray());
            case JsonValue v when v.TryGetValue<string>(out var s) && s.Contains("${"):
                var whole = Ref().Match(s);
                if (whole.Success && whole.Length == s.Length) return Lookup(whole, runParams, outputs)?.DeepClone();
                return JsonValue.Create(Ref().Replace(s, m => Lookup(m, runParams, outputs) switch
                {
                    JsonValue jv when jv.TryGetValue<string>(out var str) => str,
                    null => "",
                    var other => other.ToJsonString(),
                }));
            default:
                return node?.DeepClone();
        }
    }

    private static JsonNode? Lookup(Match m, JsonObject runParams, IReadOnlyDictionary<string, JsonObject> outputs) =>
        m.Groups[1].Value == "params" ? runParams[m.Groups[2].Value]
        : outputs.TryGetValue(m.Groups[2].Value, out var o) ? o[m.Groups[3].Success ? m.Groups[3].Value : "result"] : null;

    public static async Task<Guid> CreateRunAsync(IWriteSession db, IClock clock, PipelineRow p, JsonObject? overrides, string trigger, Guid createdBy, CancellationToken ct)
    {
        var (defaults, steps) = Parse(p.Definition);
        foreach (var (k, v) in overrides ?? []) defaults[k] = v?.DeepClone();
        var id = Ids.New();
        await db.InsertPipelineRunAsync(id, p.Id, p.WorkspaceId, defaults.ToJsonString(), trigger, createdBy, clock.UtcNow, ct);
        foreach (var s in steps) await db.InsertStepAsync(id, s.Id, s.Kind, ct);
        return id;
    }
}
