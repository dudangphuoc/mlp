using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MLPlatform.Shim;

public readonly record struct MetricPoint(string Key, long Step, double Value);

/// <summary>
/// Metric collection without changing the repo (20/08 §5.3): incrementally reads (by offset) the sources declared in the manifest, mapping keys
/// via <c>metrics.map</c>. Call <see cref="Poll"/> periodically and once more when the process exits.
/// </summary>
public sealed class MetricCollector
{
    private readonly List<Func<IEnumerable<MetricPoint>>> _sources = [];
    private readonly Dictionary<string, string> _map;
    private readonly HashSet<(string, long)> _seen = [];
    private readonly List<(Regex Rx, string Stream)> _regex = [];
    private readonly Queue<MetricPoint> _fromStreams = new();
    private readonly List<Regex> _epochKeys = [];
    private readonly List<MetricPoint> _pending = [];
    private readonly HashSet<(string, long)> _invalidSeen = [];

    /// <summary>R25: epoch index → global step ((epoch + 1) × steps per epoch); null while steps per epoch is unknown (points wait).</summary>
    public Func<long, long?>? EpochToStep { get; set; }
    /// <summary>Step for values read from output lines (regex sources): the training's current global step.</summary>
    public Func<long>? CurrentStep { get; set; }
    /// <summary>R26: a non-finite value (NaN/inf) is reported once per key and step instead of being dropped silently.</summary>
    public Action<string, long>? OnInvalid { get; set; }

    public MetricCollector(JsonObject metrics, Func<string, string> render)
    {
        _map = (metrics["map"] as JsonObject)?.ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>()) ?? [];
        foreach (var g in (metrics["epochKeys"] as JsonArray)?.Select(x => x?.GetValue<string>()).OfType<string>() ?? [])
            _epochKeys.Add(new Regex("^" + Regex.Escape(g).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.Compiled));
        foreach (var src in (metrics["sources"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            foreach (var (kind, node) in src)
            {
                switch (kind)
                {
                    case "jsonl": _sources.Add(Jsonl(render(node!.GetValue<string>()))); break;
                    case "csv": _sources.Add(Csv(render(node!.GetValue<string>()))); break;
                    case "json":
                        var parts = render(node!.GetValue<string>()).Split('#', 2);
                        _sources.Add(JsonArrayFile(parts[0], parts.Length > 1 ? parts[1] : ""));
                        break;
                    case "tensorboard": _sources.Add(TensorBoard(render(node!.GetValue<string>()))); break;
                    case "regex":
                        var o = node!.AsObject();
                        _regex.Add((new Regex(o["pattern"]!.GetValue<string>(), RegexOptions.Compiled, TimeSpan.FromMilliseconds(50)),
                            o["stream"]?.GetValue<string>() ?? "stdout"));
                        break;
                }
            }
        }
    }

    /// <summary>Output line of the child process (regex source).</summary>
    public void OnLine(string stream, string line)
    {
        foreach (var (rx, s) in _regex)
        {
            if (s != stream) continue;
            Match m;
            try { m = rx.Match(line); } catch (RegexMatchTimeoutException) { continue; }
            if (!m.Success) continue;
            foreach (var g in m.Groups.Keys)
            {
                if (int.TryParse(g, out _)) continue;
                if (m.Groups[g].Success && double.TryParse(m.Groups[g].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    lock (_fromStreams) _fromStreams.Enqueue(new MetricPoint(g, CurrentStep?.Invoke() ?? 0, v));
            }
        }
    }

    public List<MetricPoint> Poll()
    {
        var res = new List<MetricPoint>();
        if (_pending.Count > 0 && EpochToStep?.Invoke(0) is not null)
        {
            var waiting = _pending.ToList();
            _pending.Clear();
            foreach (var p in waiting) Add(res, p);
        }
        foreach (var s in _sources)
            foreach (var p in s())
                Add(res, p);
        lock (_fromStreams)
            while (_fromStreams.TryDequeue(out var p)) Add(res, p, dedupe: false);
        return res;
    }

    private void Add(List<MetricPoint> res, MetricPoint p, bool dedupe = true)
    {
        var mapped = p with { Key = Map(p.Key) };
        if (!double.IsFinite(p.Value))
        {
            if (_invalidSeen.Add((mapped.Key, p.Step))) OnInvalid?.Invoke(mapped.Key, p.Step);
            return;
        }
        if (_epochKeys.Count > 0 && _epochKeys.Any(rx => rx.IsMatch(p.Key)))
        {
            if (EpochToStep?.Invoke(p.Step) is not { } step) { _pending.Add(p); return; }
            mapped = mapped with { Step = step };
        }
        if (!dedupe || _seen.Add((mapped.Key, mapped.Step))) res.Add(mapped);
    }

    private string Map(string k) => _map.TryGetValue(k, out var v) ? v : k;

    // ---- sources ----

    private static Func<IEnumerable<MetricPoint>> Jsonl(string path)
    {
        long offset = 0;
        return () => ReadNewLines(path, ref offset).SelectMany(line =>
        {
            if (JsonNode.Parse(line) is not JsonObject o) return [];
            var step = Step(o);
            return Flatten(o, "").Select(kv => new MetricPoint(kv.Key, step, kv.Value));
        }).ToList();
    }

    private static Func<IEnumerable<MetricPoint>> Csv(string path)
    {
        long offset = 0;
        string[]? header = null;
        var row = 0L;
        return () =>
        {
            var pts = new List<MetricPoint>();
            foreach (var line in ReadNewLines(path, ref offset))
            {
                var cells = line.Split(',').Select(c => c.Trim()).ToArray();
                if (header is null) { header = cells; continue; }
                row++;
                var step = row;
                var si = Array.FindIndex(header, h => h is "epoch" or "step");
                if (si >= 0 && double.TryParse(cells.ElementAtOrDefault(si), NumberStyles.Float, CultureInfo.InvariantCulture, out var sv)) step = (long)sv;
                for (var i = 0; i < Math.Min(header.Length, cells.Length); i++)
                    if (i != si && double.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        pts.Add(new MetricPoint(header[i], step, v));
            }
            return pts;
        };
    }

    private static Func<IEnumerable<MetricPoint>> JsonArrayFile(string path, string pointer)
    {
        DateTime last = default;
        return () =>
        {
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) == last) return [];
            last = File.GetLastWriteTimeUtc(path);
            JsonNode? n;
            try { n = JsonNode.Parse(File.ReadAllText(path)); } catch (System.Text.Json.JsonException) { return []; }
            foreach (var part in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)) n = n?[part];
            return (n as JsonArray)?.OfType<JsonObject>().SelectMany(o =>
                Flatten(o, "").Select(kv => new MetricPoint(kv.Key, Step(o), kv.Value))).ToList() ?? [];
        };
    }

    /// <summary>TensorBoard: TFRecord (len u64, crc, data, crc) containing Event protos; reads the minimal wire format.</summary>
    private static Func<IEnumerable<MetricPoint>> TensorBoard(string dir)
    {
        var offsets = new Dictionary<string, long>();
        return () =>
        {
            var pts = new List<MetricPoint>();
            if (!Directory.Exists(dir)) return pts;
            foreach (var f in Directory.EnumerateFiles(dir, "events.out.tfevents.*", SearchOption.AllDirectories))
            {
                offsets.TryGetValue(f, out var off);
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Position = off;
                var header = new byte[12];
                while (fs.Read(header, 0, 12) == 12)
                {
                    var len = (long)BinaryPrimitives.ReadUInt64LittleEndian(header);
                    if (len > 64 * 1024 * 1024 || fs.Position + len + 4 > fs.Length) { fs.Position -= 12; break; }
                    var data = new byte[len];
                    fs.ReadExactly(data);
                    fs.Position += 4;
                    pts.AddRange(TfEvent.Parse(data));
                }
                offsets[f] = fs.Position;
            }
            return pts;
        };
    }

    private static List<string> ReadNewLines(string path, ref long offset)
    {
        if (!File.Exists(path)) return [];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length < offset) offset = 0;   // file was rewritten
        fs.Position = offset;
        using var sr = new StreamReader(fs, Encoding.UTF8);
        var text = sr.ReadToEnd();
        var lastNl = text.LastIndexOf('\n');
        if (lastNl < 0) return [];
        offset += Encoding.UTF8.GetByteCount(text.AsSpan(0, lastNl + 1));
        return text[..lastNl].Split('\n').Where(l => l.Trim().Length > 0).ToList();
    }

    private static long Step(JsonObject o)
    {
        foreach (var k in new[] { "step", "global_step", "epoch" })
            if (o[k] is JsonValue v && v.TryGetValue<double>(out var d)) return (long)d;
        return 0;
    }

    /// <summary>Number → key; array of numbers → key <c>a[i]</c> (e.g. test_coco_eval_bbox[0]).</summary>
    private static IEnumerable<KeyValuePair<string, double>> Flatten(JsonObject o, string prefix)
    {
        foreach (var (k, v) in o)
        {
            if (k is "step" or "global_step" or "epoch") continue;
            var key = prefix + k;
            switch (v)
            {
                case JsonValue jv when jv.TryGetValue<double>(out var d): yield return new(key, d); break;
                case JsonArray arr:
                    for (var i = 0; i < arr.Count; i++)
                        if (arr[i] is JsonValue av && av.TryGetValue<double>(out var ad)) yield return new($"{key}[{i}]", ad);
                    break;
            }
        }
    }
}

/// <summary>Decode Event{step=2, summary=5{value=1{tag=1, simple_value=2, tensor=8{float_val=5, double_val=6, tensor_content=4}}}}.</summary>
internal static class TfEvent
{
    public static IEnumerable<MetricPoint> Parse(ReadOnlySpan<byte> ev)
    {
        long step = 0;
        var values = new List<(string, double)>();
        var r = new Wire(ev.ToArray());
        while (r.Next(out var field, out var type))
        {
            if (field == 2 && type == 0) step = (long)r.Varint();
            else if (field == 5 && type == 2) ParseSummary(r.Bytes(), values);
            else r.Skip(type);
        }
        return values.Select(v => new MetricPoint(v.Item1, step, v.Item2)).ToList();
    }

    private static void ParseSummary(byte[] s, List<(string, double)> values)
    {
        var r = new Wire(s);
        while (r.Next(out var f, out var t))
        {
            if (f != 1 || t != 2) { r.Skip(t); continue; }
            var v = new Wire(r.Bytes());
            string tag = "";
            double? val = null;
            while (v.Next(out var vf, out var vt))
            {
                if (vf == 1 && vt == 2) tag = Encoding.UTF8.GetString(v.Bytes());
                else if (vf == 2 && vt == 5) val = v.Float();
                else if (vf == 8 && vt == 2) val = ParseTensor(v.Bytes()) ?? val;
                else v.Skip(vt);
            }
            if (val is { } d && tag.Length > 0) values.Add((tag, d));
        }
    }

    private static double? ParseTensor(byte[] t)
    {
        var r = new Wire(t);
        int dtype = 0;
        byte[]? content = null;
        double? val = null;
        while (r.Next(out var f, out var ty))
        {
            if (f == 1 && ty == 0) dtype = (int)r.Varint();
            else if (f == 4 && ty == 2) content = r.Bytes();
            else if (f == 5 && ty == 2) { var b = r.Bytes(); if (b.Length >= 4) val = BinaryPrimitives.ReadSingleLittleEndian(b); }
            else if (f == 5 && ty == 5) val = r.Float();
            else if (f == 6 && ty == 2) { var b = r.Bytes(); if (b.Length >= 8) val = BinaryPrimitives.ReadDoubleLittleEndian(b); }
            else if (f == 6 && ty == 1) val = r.Double();
            else r.Skip(ty);
        }
        if (val is null && content is not null)
            val = dtype == 2 && content.Length >= 8 ? BinaryPrimitives.ReadDoubleLittleEndian(content)
                : content.Length >= 4 ? BinaryPrimitives.ReadSingleLittleEndian(content) : null;
        return val;
    }

    private sealed class Wire(byte[] b)
    {
        private int _p;
        public bool Next(out int field, out int type)
        {
            field = type = 0;
            if (_p >= b.Length) return false;
            var key = Varint();
            field = (int)(key >> 3);
            type = (int)(key & 7);
            return true;
        }
        public ulong Varint()
        {
            ulong res = 0;
            for (var shift = 0; shift < 64 && _p < b.Length; shift += 7)
            {
                var x = b[_p++];
                res |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
            }
            return res;
        }
        public byte[] Bytes() { var n = (int)Varint(); var r = b.AsSpan(_p, n).ToArray(); _p += n; return r; }
        public float Float() { var v = BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(_p)); _p += 4; return v; }
        public double Double() { var v = BinaryPrimitives.ReadDoubleLittleEndian(b.AsSpan(_p)); _p += 8; return v; }
        public void Skip(int type)
        {
            switch (type)
            {
                case 0: Varint(); break;
                case 1: _p += 8; break;
                case 2: _p += (int)Varint(); break;
                case 5: _p += 4; break;
                default: _p = b.Length; break;
            }
        }
    }
}
