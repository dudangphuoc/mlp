using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MLPlatform.Recipes;

/// <summary>Manifest selfcheck (M17 R2a): every template renders with sample values, placeholders are in the 20/08 §4.1
/// catalog, parameters are declared, `{input.*}` is declared.</summary>
public static partial class RecipeChecker
{
    [GeneratedRegex(@"^(data|data\.(train|val|test)|dataset\.classes\.(count|names_csv|json_path)|dataset\.target_column|output|checkpoint|weights|weights\.ref|model|model\.weights|base_weights|zoo\.[a-z0-9_]+|recipe\.src|variant\.[a-z_]+|task|format|params\.[a-z0-9_]+|params\.json|input\.[a-z_]+|nnodes|node_rank|gpus_per_node|gpu_ids_csv|master_addr|master_port|device|gpu\.compute_capability|gpu\.supports_bf16|converter\.(src|dst))$")]
    private static partial Regex Catalog();

    public static List<string> Check(JsonObject m, IReadOnlySet<string> knownTasks)
    {
        var errs = new List<string>();
        var name = m["metadata"]?["name"]?.GetValue<string>() ?? "?";
        var tasks = (m["tasks"] as JsonArray)?.Select(t => t!.GetValue<string>()).ToList() ?? [];
        foreach (var t in tasks.Where(t => !knownTasks.Contains(t))) errs.Add($"{name}: unknown task '{t}'");
        var paramNames = (m["params"] as JsonObject)?.Select(p => p.Key).ToHashSet() ?? [];
        var variants = (m["variants"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var entries = m["entrypoints"] as JsonObject ?? [];

        void Try(string where, string template, bool weightsEmpty, string task, JsonObject? entry, RenderMode mode = RenderMode.Shell)
        {
            try
            {
                Template.Render(template, (string n, out JsonNode? v) =>
                {
                    v = null;
                    if (!Catalog().IsMatch(n)) return false;
                    if (n.StartsWith("params.", StringComparison.Ordinal) && n != "params.json" && !paramNames.Contains(n[7..]))
                        return false;
                    if (n.StartsWith("input.", StringComparison.Ordinal)
                        && (entry?["inputs"] as JsonObject)?.ContainsKey(n[6..]) != true) return false;
                    v = n switch
                    {
                        "weights" or "weights.ref" => weightsEmpty ? null : JsonValue.Create("/mnt/weights/w"),
                        "task" => JsonValue.Create(task),
                        "gpu.supports_bf16" => JsonValue.Create(true),
                        "nnodes" or "node_rank" or "gpus_per_node" => JsonValue.Create(1),
                        _ when n.StartsWith("params.", StringComparison.Ordinal) => SampleParam(m, n[7..]),
                        _ => JsonValue.Create("/x/" + n),
                    };
                    return true;
                }, mode);
            }
            catch (TemplateException e)
            {
                errs.Add($"{name}.{where} [task={task}{(weightsEmpty ? ", empty weights" : "")}]: {e.Reason} — {e.Message}");
            }
        }

        foreach (var task in tasks.DefaultIfEmpty(""))
        {
            foreach (var (ep, node) in entries)
            {
                if (node is not JsonObject e) continue;
                if (e["command"]?.GetValue<string>() is { } cmd)
                {
                    var allWeighted = variants.Count > 0 && variants.All(v => v["weights"] is not null);
                    Try($"{ep}.command", cmd, false, task, e);
                    if (ep == "train" && (e["fromScratch"]?.GetValue<bool>() == true || !allWeighted))
                        Try($"{ep}.command", cmd, true, task, e);
                    if (ep != "train" && cmd.Contains("{weights}", StringComparison.Ordinal))
                        errs.Add($"{name}.{ep}: {{weights}} used outside train; use {{model.weights}}");
                }
                if (e["resume"] is JsonObject r)
                    foreach (var k in new[] { "args", "from", "to", "command" })
                        if (r[k]?.GetValue<string>() is { } rv) Try($"{ep}.resume.{k}", rv, false, task, e);
                if (e["env"] is JsonObject env)
                    foreach (var (k, v) in env) Try($"{ep}.env.{k}", v!.GetValue<string>(), false, task, e, RenderMode.Raw);
                if (e["metrics"]?["primary"]?.GetValue<string>() is { } ep2) Try($"{ep}.metrics.primary", ep2, false, task, e, RenderMode.Raw);
            }
            if (m["metrics"]?["primary"]?.GetValue<string>() is { } pm) Try("metrics.primary", pm, false, task, null, RenderMode.Raw);
            if (m["artifacts"]?["baseWeights"]?.GetValue<string>() is { } bw)
            {
                Try("artifacts.baseWeights", bw, false, task, null, RenderMode.Raw);
                if (!bw.Contains("weights.ref", StringComparison.Ordinal)) errs.Add($"{name}: baseWeights must be the ref {{weights.ref}}");
            }
            foreach (var gf in (m["data"]?["generatedFiles"] as JsonArray)?.OfType<JsonObject>() ?? [])
                Try("data.generatedFiles", gf["template"]!.GetValue<string>(), false, task, null, RenderMode.Raw);
        }
        foreach (var c in (m["converters"] as JsonArray)?.OfType<JsonObject>() ?? [])
            Try("converters.script", c["script"]!.GetValue<string>(), false, "", null);
        if (entries.ContainsKey("train") && m["artifacts"]?["best"]?["from"] is null)
            errs.Add($"{name}: has train but is missing artifacts.best.from");
        if (!entries.ContainsKey("train") && m["artifacts"]?["best"]?["as"] is null
            && (entries.ContainsKey("serve") || entries.ContainsKey("assist") || entries.ContainsKey("inspect") || entries.ContainsKey("predict")))
            errs.Add($"{name}: inference-only recipe is missing artifacts.best.as");
        return errs;
    }

    private static JsonNode? SampleParam(JsonObject m, string p)
    {
        var def = m["params"]?[p];
        if (def?["default"] is { } d) return d.DeepClone();
        return def?["type"]?.ToString() == "boolean" ? JsonValue.Create(true) : JsonValue.Create("1");
    }
}
