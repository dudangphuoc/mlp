using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

public static class LabelRules
{
    public static readonly string[] Kinds = ["tag", "bbox", "obb", "polygon", "mask", "keypoints", "span", "segment", "relation", "text_pair", "preference", "value", "transcript"];
    public static readonly string[] Roles = ["lead", "annotator", "reviewer"];

    public static ValidationException Invalid(string field, string msg) => new(new Dictionary<string, string> { [field] = msg });

    /// <summary>classes = [{id:int, name:string, color?:string}], unique ids.</summary>
    public static HashSet<int> ParseClasses(string json)
    {
        var ids = new HashSet<int>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                throw Invalid("classes", "At least one class is required");
            foreach (var c in doc.RootElement.EnumerateArray())
            {
                if (!c.TryGetProperty("id", out var id) || !id.TryGetInt32(out var i) || !c.TryGetProperty("name", out var n)
                    || n.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(n.GetString()))
                    throw Invalid("classes", "Each class requires an id (number) and a name");
                if (!ids.Add(i)) throw Invalid("classes", $"Duplicate class id {i}");
            }
        }
        catch (JsonException) { throw Invalid("classes", "Invalid JSON"); }
        return ids;
    }

    /// <summary>Validates against the schema (R: class exists, geometry is well-formed, bbox is inside the
    /// image).</summary>
    public static void Validate(IReadOnlyList<AnnotationInput> items, HashSet<int> classes)
    {
        var errors = new Dictionary<string, string>();
        for (var i = 0; i < items.Count; i++)
        {
            var a = items[i];
            var err = Check(a, classes);
            if (err is not null) errors[$"annotations[{i}]"] = err;
        }
        if (errors.Count > 0) throw new InvalidArgumentException("LABEL_SCHEMA_VIOLATION", "Invalid labels", errors);
    }

    private static string? Check(AnnotationInput a, HashSet<int> classes)
    {
        if (!Kinds.Contains(a.Kind)) return $"kind '{a.Kind}' is not supported";
        if (a.Kind is not ("relation" or "text_pair" or "preference" or "value" or "transcript") && !classes.Contains(a.ClassId))
            return $"class {a.ClassId} is not in the schema";
        try
        {
            using var g = JsonDocument.Parse(string.IsNullOrEmpty(a.Geometry) ? "{}" : a.Geometry);
            using var _ = JsonDocument.Parse(string.IsNullOrEmpty(a.Attributes) ? "{}" : a.Attributes);
            var r = g.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return "geometry must be an object";
            switch (a.Kind)
            {
                case "bbox":
                    if (!Num(r, "x", out var x) || !Num(r, "y", out var y) || !Num(r, "w", out var w) || !Num(r, "h", out var h))
                        return "bbox requires x, y, w, h";
                    const double eps = 1e-6;
                    if (x < -eps || y < -eps || w <= 0 || h <= 0 || x + w > 1 + eps || y + h > 1 + eps) return "bbox is outside the image";
                    break;
                case "polygon":
                    if (!r.TryGetProperty("points", out var pts) || pts.ValueKind != JsonValueKind.Array || pts.GetArrayLength() < 3)
                        return "polygon requires ≥ 3 points";
                    foreach (var p in pts.EnumerateArray())
                        if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 2 || p[0].GetDouble() is < 0 or > 1 || p[1].GetDouble() is < 0 or > 1)
                            return "polygon point is outside the image";
                    break;
                case "span":
                    if (!Num(r, "start", out var s) || !Num(r, "end", out var e) || s < 0 || e <= s) return "span requires start < end";
                    break;
                case "segment":
                    if (!Num(r, "start", out var ss) || !Num(r, "end", out var se) || ss < 0 || se <= ss) return "segment requires start < end (seconds)";
                    break;
                case "keypoints":
                    if (!r.TryGetProperty("points", out var kps) || kps.ValueKind != JsonValueKind.Array || kps.GetArrayLength() == 0)
                        return "keypoints requires points";
                    foreach (var p in kps.EnumerateArray())
                        if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 2 || p[0].GetDouble() is < 0 or > 1 || p[1].GetDouble() is < 0 or > 1)
                            return "keypoint is outside the image";
                    break;
                case "mask":
                    if (!r.TryGetProperty("rle", out var rle) || !rle.TryGetProperty("size", out var size) || size.GetArrayLength() != 2
                        || !rle.TryGetProperty("counts", out var counts) || counts.ValueKind != JsonValueKind.Array)
                        return "mask requires rle.size [h, w] and rle.counts";
                    long cells = (long)size[0].GetInt32() * size[1].GetInt32(), sum = 0;
                    foreach (var n in counts.EnumerateArray()) sum += n.GetInt64();
                    if (cells <= 0 || sum != cells) return "mask counts do not cover the image";
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return "geometry/attributes is not valid JSON"; }
        if (a.Confidence is < 0 or > 1) return "confidence is outside [0, 1]";
        return a.Source is "human" or "model" or "import" or "ai" ? null : "source must be human/model/import/ai";
    }

    private static bool Num(JsonElement o, string name, out double v)
    {
        v = 0;
        return o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out v);
    }

    /// <summary>Label list → JSON (for history/drafts/label sets) — Utf8JsonWriter, no reflection (AOT).</summary>
    public static string ToJson(IEnumerable<(string Kind, int ClassId, string Geometry, string Attributes, string Source)> items)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var a in items) WriteOne(w, null, a.Kind, a.ClassId, a.Geometry, a.Attributes, a.Source);
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static void WriteOne(Utf8JsonWriter w, string? path, string kind, int classId, string geometry, string attributes, string source)
    {
        w.WriteStartObject();
        if (path is not null) w.WriteString("path", path);
        w.WriteString("kind", kind);
        w.WriteNumber("class_id", classId);
        w.WritePropertyName("geometry"); w.WriteRawValue(string.IsNullOrEmpty(geometry) ? "{}" : geometry);
        w.WritePropertyName("attributes"); w.WriteRawValue(string.IsNullOrEmpty(attributes) ? "{}" : attributes);
        w.WriteString("source", source);
        w.WriteEndObject();
    }
}
