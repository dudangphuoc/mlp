using System.Text.Json.Nodes;
using MLPlatform.Recipes;

namespace MLPlatform.Shim;

/// <summary>Specification of one attempt (the control plane writes the JSON, the agent mounts it into the container;
/// 20/08 §5.1).</summary>
public sealed class JobSpec
{
    public required JsonObject Manifest { get; init; }
    public required string Entrypoint { get; init; }
    public string Task { get; init; } = "";
    public JsonObject Variant { get; init; } = [];
    public JsonObject Params { get; init; } = [];
    public required string Output { get; init; }
    public string? Data { get; set; }
    public string? Weights { get; init; }
    public string? WeightsRef { get; init; }
    public string? Model { get; init; }
    public string? BaseWeights { get; init; }
    public string? Checkpoint { get; set; }
    public string? Format { get; init; }
    public JsonObject Inputs { get; init; } = [];
    public JsonObject Dataset { get; init; } = [];
    public JsonObject Hardware { get; init; } = [];
    public JsonObject Zoo { get; init; } = [];
    public string RecipeSrc { get; init; } = "/opt/recipe/src";
    public string? ConverterSrc { get; init; }
    public string? ConverterDst { get; init; }
    public JsonArray DataFiles { get; init; } = [];
    public string? DataFilesUrl { get; init; }
    public string? DataCache { get; init; }
    public string? DatasetId { get; init; }
    public string? DatasetVersionId { get; init; }
    public string? WeightsUrl { get; init; }
    public JsonObject? Trial { get; init; }

    public static JobSpec Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Spec via environment variable (the api sets MLP_JOB_SPEC when submitting a training job; M09).</summary>
    public static JobSpec FromEnvironment(string name) =>
        Parse(Environment.GetEnvironmentVariable(name) ?? throw new ShimException(3, "spec_missing", $"Missing variable {name}"));

    public static JobSpec Parse(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        string? S(string k) => o[k]?.GetValue<string>();
        JsonObject O(string k) => o[k] as JsonObject ?? [];
        var manifest = O("manifest");
        if (manifest.Count == 0 && S("manifestPath") is { } mp)
            manifest = YamlJson.Parse(File.ReadAllText(mp)) as JsonObject ?? [];
        return new JobSpec
        {
            Manifest = manifest, Entrypoint = S("entrypoint") ?? "train", Task = S("task") ?? "",
            Variant = O("variant"), Params = O("params"), Output = S("output") ?? "/mnt/output", Data = S("data"),
            Weights = S("weights"), WeightsRef = S("weightsRef"), Model = S("model"), BaseWeights = S("baseWeights"),
            Checkpoint = S("checkpoint"), Format = S("format"), Inputs = O("inputs"), Dataset = O("dataset"),
            Hardware = O("hardware"), Zoo = O("zoo"), RecipeSrc = S("recipeSrc") ?? "/opt/recipe/src",
            ConverterSrc = S("converterSrc"), ConverterDst = S("converterDst"), DataFiles = o["dataFiles"] as JsonArray ?? [],
            DataFilesUrl = S("dataFilesUrl"), WeightsUrl = S("weightsUrl"), Trial = o["trial"] as JsonObject,
            DataCache = S("dataCache"), DatasetId = S("datasetId"), DatasetVersionId = S("datasetVersionId"),
        };
    }

    public JsonObject Entry => Manifest["entrypoints"]?[Entrypoint] as JsonObject
        ?? throw new ShimException(3, "recipe_entrypoint_missing", $"Recipe has no entrypoint '{Entrypoint}'");

    /// <summary>Template resolver following the 20/08 §4.1 placeholder catalog.</summary>
    public bool Resolve(string name, out JsonNode? value)
    {
        if (TryResolveFixed(name, out value)) return true;
        if (name.StartsWith("params.", StringComparison.Ordinal)) return TryResolveParam(name[7..], out value);
        if (name.StartsWith("variant.", StringComparison.Ordinal)) { value = Variant[name[8..]]?.DeepClone(); return true; }
        if (name.StartsWith("input.", StringComparison.Ordinal))
        {
            if (Entry["inputs"]?[name[6..]] is null) return false;
            value = Inputs[name[6..]]?.DeepClone(); return true;
        }
        if (name.StartsWith("zoo.", StringComparison.Ordinal)) { value = Zoo[name[4..]]?.DeepClone(); return true; }
        if (name.StartsWith("dataset.", StringComparison.Ordinal)) { value = Dataset[name[8..]]?.DeepClone(); return true; }
        if (name is "nnodes" or "node_rank" or "gpus_per_node" or "gpu_ids_csv" or "master_addr" or "master_port" or "device"
            || name.StartsWith("gpu.", StringComparison.Ordinal))
        {
            value = Hardware[name]?.DeepClone() ?? DefaultHardware(name);
            return true;
        }
        return false;
    }

    private bool TryResolveFixed(string name, out JsonNode? value)
    {
        value = null;
        JsonNode? V(string? s) => s is null ? null : JsonValue.Create(s);
        var splitNames = Manifest["data"]?["splitNames"] as JsonObject;
        string Split(string s) => splitNames?[s]?.GetValue<string>() ?? s;
        switch (name)
        {
            case "data": value = V(Data); return true;
            case "data.train" or "data.val" or "data.test":
                value = Data is null ? null : V(SplitPath(Data, Split(name[5..]))); return true;
            case "output": value = V(Output); return true;
            case "checkpoint": value = V(Checkpoint); return true;
            case "weights": value = V(Weights); return true;
            case "weights.ref": value = V(WeightsRef); return true;
            case "model": value = V(Model); return true;
            case "model.weights":
                var asName = Manifest["artifacts"]?["best"]?["as"]?.GetValue<string>();
                value = Model is null || asName is null ? null : V(Path.Combine(Model, "weights", asName)); return true;
            case "base_weights": value = V(BaseWeights); return true;
            case "recipe.src": value = V(RecipeSrc); return true;
            case "task": value = V(Task); return true;
            case "format": value = V(Format); return true;
            case "params.json": value = Params.DeepClone(); return true;
            case "converter.src": value = V(ConverterSrc); return true;
            case "converter.dst": value = V(ConverterDst); return true;
        }
        return false;
    }

    private bool TryResolveParam(string p, out JsonNode? value)
    {
        value = null;
        if (Params.ContainsKey(p)) { value = Params[p]?.DeepClone(); return true; }
        var def = Manifest["params"]?[p];
        if (def is null) return false;
        if (def["required"]?.GetValue<bool>() == true)
            throw new ShimException(2, "param_required", $"Missing required parameter {p}");
        value = def["default"]?.DeepClone();
        return true;
    }

    private static JsonNode? DefaultHardware(string name) => name switch
    {
        "nnodes" => JsonValue.Create(1),
        "node_rank" => JsonValue.Create(0),
        "gpus_per_node" => JsonValue.Create(1),
        "master_addr" => JsonValue.Create("127.0.0.1"),
        "master_port" => JsonValue.Create(29500),
        "device" => JsonValue.Create("cpu"),
        "gpu.supports_bf16" => JsonValue.Create(false),
        _ => null,
    };

    /// <summary>A split may be a folder (`val/`) or a file (`val.jsonl`, `val.parquet`); 20/08 §7.</summary>
    private static string SplitPath(string data, string split)
    {
        var dir = Path.Combine(data, split);
        if (Directory.Exists(dir)) return dir;
        foreach (var ext in new[] { ".jsonl", ".parquet", ".conll", ".json" })
            if (File.Exists(dir + ext)) return dir + ext;
        return dir;
    }
}
