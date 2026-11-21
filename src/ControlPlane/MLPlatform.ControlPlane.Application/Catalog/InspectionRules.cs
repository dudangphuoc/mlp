using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>
/// Rules of the recipe inspection (round 1 slice 2, D-44): which parameters make a new cache entry, the command the image runs
/// (<c>canvas.inspect</c> of the manifest) and how its one <c>MLP_INSPECT {json}</c> line is read.
/// </summary>
public static class InspectionRules
{
    public const string Marker = "MLP_INSPECT ";
    /// <summary>The agent keeps the last 64 KB of stdout; an output near that size has lost its beginning.</summary>
    private const int OutputLimit = 65536;
    public static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan BuildLimit = TimeSpan.FromMinutes(70);

    /// <summary>The values a person set: a JSON object without nulls (null = "as the variant has it"). Empty text = no values.</summary>
    public static JsonObject ParseParams(string json)
    {
        var res = new JsonObject();
        if (string.IsNullOrWhiteSpace(json)) return res;
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { node = null; }
        if (node is not JsonObject o) throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Must be a JSON object" });
        foreach (var (k, v) in o.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (v is not null) res[k] = v.DeepClone();
        return res;
    }

    /// <summary>SHA-256 of the canonical JSON (keys in ordinal order) of the set parameters the manifest marks <c>affectsInspect: true</c>.</summary>
    public static string ParamsHash(JsonObject manifest, JsonObject set)
    {
        var defs = manifest["params"] as JsonObject;
        var canon = new JsonObject();
        foreach (var (k, v) in set.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (v is not null && defs?[k]?["affectsInspect"] is JsonValue f && f.TryGetValue<bool>(out var on) && on) canon[k] = v.DeepClone();
        // "v2": inspections made before the defaults were passed (2026-11-20) showed the variant's own schedule — never reuse them.
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("v2:" + canon.ToJsonString())));
    }

    public static JsonObject? Variant(JsonObject manifest, string name) =>
        (manifest["variants"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(v => v["name"]?.ToString() == name);

    /// <summary>
    /// The shell command of <c>canvas.inspect</c>: <c>{variant.x}</c> from the variant, <c>{dataset.classes.count}</c> = the class count
    /// (0 = keep the variant's own), <c>{params.json}</c> = every set value, <c>{params.x}</c> = the set value or the manifest default.
    /// </summary>
    /// <summary>
    /// The values a training would run with: what was set, plus every non-null manifest default. Without the defaults the inspection would
    /// show the variant's own schedule (e.g. 132 epochs) while the training scales it to the recipe default (72).
    /// </summary>
    public static JsonObject WithDefaults(JsonObject manifest, JsonObject set)
    {
        var res = new JsonObject();
        foreach (var (k, def) in manifest["params"] as JsonObject ?? [])
            if (def is JsonObject d && d["from"] is null && (set[k] ?? d["default"]) is { } val) res[k] = val.DeepClone();
        return res;
    }

    public static string Command(JsonObject manifest, JsonObject variant, JsonObject set, int numClasses)
    {
        var template = manifest["canvas"]?["inspect"]?.ToString() ?? "";
        return Template.Render(template.Replace('\n', ' ').Trim(), (string n, out JsonNode? v) =>
        {
            v = null;
            if (n == "dataset.classes.count") v = JsonValue.Create(numClasses);
            else if (n == "params.json") v = JsonValue.Create(WithDefaults(manifest, set).ToJsonString());
            else if (n.StartsWith("variant.", StringComparison.Ordinal))
            {
                if (!variant.ContainsKey(n[8..])) return false;
                v = variant[n[8..]]?.DeepClone();
            }
            else if (n.StartsWith("params.", StringComparison.Ordinal))
            {
                if (manifest["params"]?[n[7..]] is not JsonObject def) return false;
                v = (set[n[7..]] ?? def["default"])?.DeepClone();
            }
            else return false;
            return true;
        });
    }

    /// <summary>Reads the last <c>MLP_INSPECT</c> line of stdout. <paramref name="stderr"/> explains a failed run.</summary>
    public static InspectionOutput Parse(string stdout, bool exitOk, string stderr, Func<IReadOnlyList<string>, string?> headline)
    {
        static InspectionOutput Fail(string error) => new(false, error.Length > 500 ? error[..500] + "…" : error, "", "", "[]");
        if (!exitOk)
        {
            var lines = (stderr + "\n" + stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Fail(headline(lines) ?? lines.LastOrDefault(l => !l.StartsWith(Marker, StringComparison.Ordinal)) ?? "The inspection exited with an error");
        }
        var line = stdout.Split('\n').LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
        if (line is null)
            return Fail(stdout.Length >= OutputLimit - 1024 ? "inspection output too large" : "The inspection printed no MLP_INSPECT line");
        JsonObject? doc;
        try { doc = JsonNode.Parse(line[Marker.Length..].Trim()) as JsonObject; }
        catch (JsonException) { doc = null; }
        if (doc is null) return Fail("inspection output too large");
        var blocks = new JsonArray();
        foreach (var b in (doc["blocks"] as JsonArray)?.OfType<JsonObject>() ?? [])
            blocks.Add((JsonNode)new JsonObject { ["key"] = b["key"]?.ToString() ?? "", ["params"] = Long(b["params"]), ["trainable"] = Long(b["trainable"]) });
        return new InspectionOutput(true, "", doc["overlay_yaml"]?.ToString() ?? "", doc["config"]?.ToJsonString() ?? "{}", blocks.ToJsonString(),
            Sources(doc["sources_gz"]?.ToString()));
    }

    /// <summary>
    /// <c>sources_gz</c> = base64 of gzipped JSON <c>{pane: [{name, file, line, code}]}</c>. Only that shape is kept; anything unreadable
    /// gives <c>{}</c> (the canvas then has no Code level for the pane).
    /// </summary>
    public static string Sources(string? gz)
    {
        if (string.IsNullOrEmpty(gz)) return "{}";
        try
        {
            using var input = new MemoryStream(Convert.FromBase64String(gz));
            using var unzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var limited = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = unzip.Read(buf, 0, buf.Length)) > 0)
            {
                limited.Write(buf, 0, n);
                if (limited.Length > 2_000_000) return "{}";
            }
            if (JsonNode.Parse(limited.ToArray()) is not JsonObject doc) return "{}";
            var res = new JsonObject();
            foreach (var (pane, list) in doc)
            {
                var items = new JsonArray();
                foreach (var s in (list as JsonArray)?.OfType<JsonObject>() ?? [])
                    items.Add((JsonNode)new JsonObject
                    {
                        ["name"] = s["name"]?.ToString() ?? "", ["file"] = s["file"]?.ToString() ?? "", ["line"] = Long(s["line"]), ["code"] = s["code"]?.ToString() ?? "",
                    });
                if (items.Count > 0) res[pane] = items;
            }
            return res.ToJsonString();
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or JsonException) { return "{}"; }
    }

    public static long Long(JsonNode? n) =>
        n is JsonValue v ? v.TryGetValue<long>(out var l) ? l : v.TryGetValue<double>(out var d) ? (long)d : 0 : 0;
}
