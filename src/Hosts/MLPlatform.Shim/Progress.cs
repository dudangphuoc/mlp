using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MLPlatform.Shim;

/// <summary>
/// Training progress read from the recipe's own output lines (round 1 R24): `progress.pattern` gives epoch e/E, step i/n and the line's loss;
/// `progress.validatePattern` marks the validation sub-phase. Epochs are 0-based in the pattern (D-FINE prints "Epoch: [3/24]" for the 4th epoch).
/// ETA = measured seconds per epoch (training + validation) × epochs left; before a full epoch was seen, the step rate of the current epoch.
/// </summary>
public sealed class TrainProgress
{
    private readonly Regex? _train;
    private readonly Regex? _validate;
    private readonly object _gate = new();
    private readonly Dictionary<long, DateTimeOffset> _epochStart = [];
    private DateTimeOffset? _stepClockAt;
    private long _stepClockStep;

    public long Epoch { get; private set; } = -1;
    public long Epochs { get; private set; }
    public long Step { get; private set; }
    public long Steps { get; private set; }
    public double? Loss { get; private set; }
    public string Sub { get; private set; } = "";
    public long ValStep { get; private set; }
    public long ValSteps { get; private set; }
    /// <summary>Epochs whose training part finished (the last step line of the epoch was seen).</summary>
    public long CompletedEpochs { get; private set; }
    public double? EpochSeconds { get; private set; }
    /// <summary>Stage boundaries (e.g. D-FINE stop_epoch) read from the generated config: key, label, epoch it ends at.</summary>
    public JsonArray? Stages { get; set; }

    public TrainProgress(JsonObject? def)
    {
        if (def?["pattern"]?.GetValue<string>() is { Length: > 0 } p) _train = new Regex(p, RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));
        if (def?["validatePattern"]?.GetValue<string>() is { Length: > 0 } v) _validate = new Regex(v, RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));
    }

    public bool Enabled => _train is not null;
    public bool Known => Enabled && Epoch >= 0 && Epochs > 0 && Steps > 0;

    /// <summary>Global step reached (1-based count of finished steps); the axis for epoch metrics (R25).</summary>
    public long GlobalStep => Known ? Epoch * Steps + Step + 1 : 0;

    public void OnLine(string line)
    {
        if (_train is null) return;
        Match m;
        try { m = _train.Match(line); } catch (RegexMatchTimeoutException) { return; }
        var now = TimeProvider.System.GetUtcNow();
        lock (_gate)
        {
            if (m.Success)
            {
                var e = L(m, "epoch");
                if (e is null) return;
                if (e != Epoch)
                {
                    _epochStart.TryAdd(e.Value, now);
                    // Epoch duration (train + validation) from two consecutive starts seen by this process.
                    if (_epochStart.TryGetValue(e.Value - 1, out var prev)) EpochSeconds = (now - prev).TotalSeconds;
                    _stepClockAt = null;
                }
                Epoch = e.Value;
                Epochs = L(m, "epochs") ?? Epochs;
                Step = L(m, "step") ?? 0;
                Steps = L(m, "steps") ?? Steps;
                Sub = "train";
                if (m.Groups["loss"] is { Success: true } lg)
                    Loss = double.TryParse(lg.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lv) ? lv
                        : lg.Value.Contains("nan", StringComparison.OrdinalIgnoreCase) ? double.NaN : lg.Value.Contains("inf", StringComparison.OrdinalIgnoreCase) ? double.PositiveInfinity : Loss;
                if (Steps > 0 && Step >= Steps - 1) CompletedEpochs = Math.Max(CompletedEpochs, Epoch + 1);
                _stepClockAt ??= now;
                if (_stepClockAt == now) _stepClockStep = Step;
                return;
            }
            if (_validate is null) return;
            Match v;
            try { v = _validate.Match(line); } catch (RegexMatchTimeoutException) { return; }
            if (!v.Success) return;
            Sub = "validate";
            ValStep = L(v, "step") ?? ValStep;
            ValSteps = L(v, "steps") ?? ValSteps;
        }
    }

    /// <summary>Seconds left: per-epoch time once measured, otherwise the current epoch's step rate (validation not included yet).</summary>
    public double EtaSeconds()
    {
        lock (_gate)
        {
            if (!Known) return 0;
            var inEpoch = Sub == "validate" ? 1.0 : Math.Clamp((Step + 1) / (double)Steps, 0, 1);
            var left = Math.Max(0, Epochs - Epoch - inEpoch);
            if (EpochSeconds is { } es and > 0) return left * es;
            if (_stepClockAt is { } at && Step > _stepClockStep)
            {
                var perStep = (TimeProvider.System.GetUtcNow() - at).TotalSeconds / (Step - _stepClockStep);
                return left * Steps * perStep;
            }
            return 0;
        }
    }

    public JsonObject Detail()
    {
        lock (_gate)
        {
            var o = new JsonObject
            {
                ["epoch"] = Epoch, ["epochs"] = Epochs, ["step"] = Step, ["steps"] = Steps, ["sub"] = Sub, ["completed_epochs"] = CompletedEpochs,
            };
            if (Loss is { } l) o["loss"] = double.IsFinite(l) ? Math.Round(l, 5) : JsonValue.Create(double.IsNaN(l) ? "nan" : "inf");
            if (Sub == "validate") { o["val_step"] = ValStep; o["val_steps"] = ValSteps; }
            if (EpochSeconds is { } es) o["epoch_seconds"] = Math.Round(es, 1);
            if (Stages is not null) o["stages"] = Stages.DeepClone();
            return o;
        }
    }

    private static long? L(Match m, string group) =>
        m.Groups[group] is { Success: true } g && long.TryParse(g.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>
    /// `canvas.phases[].untilKey` (e.g. train_dataloader.collate_fn.stop_epoch) → the epoch each stage ends at, read from the generated config
    /// (`progress.config`) by the key's last segment — the platform writes that file itself, so a flat `name: value` line is enough.
    /// </summary>
    public static JsonArray? ReadStages(JsonArray? phases, string? configPath, long epochs)
    {
        if (phases is null || phases.Count == 0 || configPath is null || !File.Exists(configPath)) return null;
        var text = File.ReadAllText(configPath);
        var res = new JsonArray();
        foreach (var p in phases.OfType<JsonObject>())
        {
            long? until = null;
            if (p["untilKey"]?.GetValue<string>() is { Length: > 0 } key)
            {
                var name = Regex.Escape(key[(key.LastIndexOf('.') + 1)..]);
                var m = Regex.Match(text, @"(?m)^\s*" + name + @":\s*(-?\d+)\s*$", RegexOptions.None, TimeSpan.FromMilliseconds(200));
                if (m.Success && long.TryParse(m.Groups[1].Value, out var u)) until = u;
            }
            res.Add((JsonNode)new JsonObject { ["key"] = p["key"]?.DeepClone(), ["label"] = p["label"]?.DeepClone(), ["until"] = until ?? (epochs > 0 ? epochs : null) });
        }
        return res;
    }
}
