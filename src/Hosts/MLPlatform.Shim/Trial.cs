using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.Recipes;

namespace MLPlatform.Shim;

/// <summary>
/// Trial mode (round 1 R15–R22): the same training on a small subset, in two natural passes — pass 1 ends after one stage-1 epoch,
/// pass 2 resumes from its checkpoint into stage 2 — then packaging and a reload of the model through the serve handler.
/// Every stage prints one <c>[mlp-trial] {json}</c> line when it starts and one when it ends; the control plane reads them from the job log.
/// </summary>
public sealed partial class Runner
{
    private static readonly string[] TrialKeys = ["data", "stage1", "weights", "resume", "package", "reload"];
    private bool _trialFailed;
    private string? _trialError;

    private async Task<int> RunTrialAsync(TrackingSink? sink, CancellationToken ct)
    {
        var entry = spec.Entry;
        var trial = spec.Trial!;
        var pretrained = spec.Weights is not null;
        foreach (var k in TrialKeys) TrialLine(k, "pending", 0, "");
        int trainImages = 0, valImages = 0;
        var pass1 = new List<string>();
        var pass2 = new List<string>();
        // The first declared checkpoint is the one a resume starts from (last.pth); `checkpoints` may be one path or a list (R28).
        string? checkpoint = CheckpointGlobs(entry).FirstOrDefault() is { } ck ? Path.Combine(spec.Output, ck) : null;

        await StageAsync("data", async () =>
        {
            await DownloadDataAsync(ct);
            await ConvertDataAsync(ct);
            await DownloadWeightsAsync(ct);
            (trainImages, valImages) = (CountImages("train"), CountImages("val"));
            return (true, $"{trainImages} train and {valImages} val images ready");
        });

        await StageAsync("stage1", async () =>
        {
            // A retried attempt must not pass on files of the previous one.
            foreach (var f in BestNames().Append(checkpoint ?? "").Where(f => f.Length > 0))
                foreach (var p in Glob(spec.Output, f)) if (File.Exists(p)) File.Delete(p);
            spec.Checkpoint = null;
            var code = await PassAsync(entry, trial["overrides"] as JsonObject, sink, pass1, ct);
            if (code != 0) return (false, ExitDetail(code));
            if (checkpoint is not null && !File.Exists(checkpoint)) return (false, $"no checkpoint {Path.GetFileName(checkpoint)} after stage 1");
            if (BestNames().Count > 0 && !BestNames().Any(b => Glob(spec.Output, b).Any()))
                return (false, $"no best checkpoint ({string.Join(", ", BestNames())}) after stage 1");
            var primary = PrimaryMetric();
            if (primary.Length > 0 && !(_lastValue.TryGetValue(primary, out var v) && double.IsFinite(v)))
                return (false, $"metric {primary} was not reported");
            return (true, primary.Length > 0 ? $"one epoch done, {primary} = {_lastValue[primary].ToString("0.####", CultureInfo.InvariantCulture)}" : "one epoch done");
        });

        if (pretrained)
            await StageAsync("weights", () =>
            {
                var (loaded, created) = TrialLog.WeightsLoad(pass1);
                if (created is null) return Task.FromResult((true, "skipped:the training did not print which layers it loaded"));
                var classes = spec.Dataset["classes.count"]?.ToString();
                var text = loaded is { } l ? $"{l} layers loaded, {created} created new" : $"{created} layers created new";
                return Task.FromResult((true, classes is { Length: > 0 } ? $"{text} for {classes} classes" : text));
            });
        else TrialLine("weights", "skipped", 0, "starts from the backbone only: no pretrained weights to load");

        await StageAsync("resume", async () =>
        {
            if (checkpoint is null || !File.Exists(checkpoint)) return (false, "no checkpoint to resume from");
            spec.Checkpoint = checkpoint;
            var code = await PassAsync(entry, trial["resumeOverrides"] as JsonObject, sink, pass2, ct);
            if (code != 0) return (false, ExitDetail(code));
            return (true, pass2.Any(l => l.Contains("Refresh EMA at epoch 1", StringComparison.Ordinal))
                ? "resumed and went through stage 2" : "resumed; stage 2 branch not seen");
        });

        await StageAsync("package", async () =>
        {
            BuildModelFolder();
            if (sink is not null) await UploadWhenReachableAsync(() => sink.UploadModelFolderAsync(Path.Combine(spec.Output, "model"), ct), _ops, ct);
            return (true, sink is null ? "model folder built (no upload: not connected)" : "model folder built and uploaded");
        });

        await StageAsync("reload", () => SelfTestAsync(ct));

        var m = TrialLog.Measure(pass1);
        var multiscale = spec.Resolve("params.multiscale", out var ms) && ms is JsonValue mv && mv.TryGetValue<bool>(out var on) && on;
        long? peak = m.PeakMb is { } pk ? (long)Math.Round(multiscale ? pk * 1.25 * 1.25 : pk) : null;
        log.WriteLine("[mlp-trial] " + new JsonObject
        {
            ["key"] = "measure", ["steps_per_s"] = m.StepsPerSecond, ["peak_vram_mb"] = peak, ["vram_estimated"] = multiscale && peak is not null,
            ["train_images"] = trainImages, ["val_images"] = valImages, ["val_s_per_batch"] = m.ValSecondsPerBatch,
        }.ToJsonString());
        log.Flush();
        if (!_trialFailed)
        {
            if (_ops is not null) await _ops.ProgressAsync("finishing", 1, 1);
            return 0;
        }
        log.WriteLine("MLP_ERROR " + new JsonObject { ["reason"] = "trial_failed", ["message"] = _trialError }.ToJsonString());
        return Fail(2, "trial_failed", _trialError ?? "Trial failed");
    }

    /// <summary>One recipe pass with its lines captured; the metric state of an earlier pass does not leak into this one.</summary>
    private async Task<int> PassAsync(JsonObject entry, JsonObject? overrides, TrackingSink? sink, List<string> lines, CancellationToken ct)
    {
        (_oomSeen, _userError, _recipeError) = (false, null, null);
        _lastValue.Clear();
        _tap = l => { lock (lines) lines.Add(l); };
        try { return await ExecuteAsync(entry, overrides, sink, ct); }
        finally { _tap = null; }
    }

    /// <summary>Runs one stage; after a failed stage the rest are skipped. Detail "skipped:…" marks the stage itself as skipped.</summary>
    private async Task StageAsync(string key, Func<Task<(bool Ok, string Detail)>> body)
    {
        if (_trialFailed) { TrialLine(key, "skipped", 0, "an earlier stage failed"); return; }
        TrialLine(key, "running", 0, "");
        var sw = Stopwatch.StartNew();
        try
        {
            var (ok, detail) = await body();
            if (ok && detail.StartsWith("skipped:", StringComparison.Ordinal)) { TrialLine(key, "skipped", sw.Elapsed.TotalSeconds, detail[8..]); return; }
            TrialLine(key, ok ? "passed" : "failed", sw.Elapsed.TotalSeconds, detail);
            if (!ok) (_trialFailed, _trialError) = (true, $"{key}: {detail}");
        }
        catch (ShimException e)
        {
            TrialLine(key, "failed", sw.Elapsed.TotalSeconds, e.Message);
            (_trialFailed, _trialError) = (true, $"{key}: {e.Message}");
            if (e.ExitCode == 1) throw;   // infrastructure (download, upload, fencing): let the scheduler retry the attempt
        }
    }

    private void TrialLine(string key, string state, double seconds, string detail)
    {
        log.WriteLine("[mlp-trial] " + new JsonObject
        {
            ["key"] = key, ["state"] = state, ["seconds"] = Math.Round(seconds, 1), ["detail"] = detail,
        }.ToJsonString());
        log.Flush();
    }

    private List<string> BestNames() =>
        (spec.Manifest["artifacts"]?["best"]?["from"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList() ?? [];

    private string PrimaryMetric() =>
        spec.Manifest["metrics"]?["primary"]?.GetValue<string>() is { } p ? Template.Render(p, spec.Resolve, RenderMode.Raw) : "";

    private string ExitDetail(int code) =>
        code == 137 || _oomSeen ? "out of GPU memory — try a smaller batch or image size"
        : _userError is not null ? _userError
        : _recipeError is not null ? "the recipe image is missing a dependency: " + _recipeError
        : $"the training process exited with code {code}";

    /// <summary>Images of one split in the (converted) data folder.</summary>
    private int CountImages(string split)
    {
        if (spec.Data is null) return 0;
        var dir = Path.Combine(spec.Data, split);
        if (!Directory.Exists(dir)) return 0;
        string[] ext = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];
        return Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Count(f => ext.Contains(Path.GetExtension(f).ToLowerInvariant()));
    }

    /// <summary>
    /// Loads the packaged model through the recipe's serve handler (python -m mlp_serve.selftest) and predicts a few val images.
    /// Anything the shim cannot provide (no handler, no mlp_serve in the image) → skipped, not failed.
    /// </summary>
    private async Task<(bool, string)> SelfTestAsync(CancellationToken ct)
    {
        var handler = spec.Manifest["entrypoints"]?["serve"]?["handler"]?.GetValue<string>();
        if (string.IsNullOrEmpty(handler)) return (true, "skipped:the recipe has no serve handler");
        var images = spec.Data is null ? null : Path.Combine(spec.Data, "val");
        if (images is null || !Directory.Exists(images)) return (true, "skipped:no val images to predict");
        var psi = new ProcessStartInfo("python") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = "/opt/recipe" };
        if (!Directory.Exists(psi.WorkingDirectory)) psi.WorkingDirectory = spec.Output;
        foreach (var a in new[] { "-m", "mlp_serve.selftest", "--model-dir", Path.Combine(spec.Output, "model"), "--images", images, "--limit", "8",
                     "--handler", handler, "--task", spec.Task, "--params-json", spec.Params.ToJsonString() })
            psi.ArgumentList.Add(a);
        psi.Environment["MLP_DEVICE"] = spec.Hardware["device"]?.ToString() ?? "cpu";
        Process? proc;
        try { proc = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception) { return (true, "skipped:python is not available in the image"); }
        if (proc is null) return (true, "skipped:could not start python");
        string? result = null, last = null;
        var missingServe = false;
        void Line(string? l)
        {
            if (l is null) return;
            log.WriteLine(l);
            if (l.StartsWith("MLP_SELFTEST ", StringComparison.Ordinal)) result = l[13..];
            else if (l.Contains("No module named 'mlp_serve", StringComparison.Ordinal)) missingServe = true;
            if (l.Trim().Length > 0) last = l.Trim();
        }
        proc.OutputDataReceived += (_, e) => Line(e.Data);
        proc.ErrorDataReceived += (_, e) => Line(e.Data);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);
        proc.WaitForExit();
        if (missingServe) return (true, "skipped:the image has no mlp_serve");
        if (result is null) return (false, last ?? $"self-test exited with code {proc.ExitCode}");
        try
        {
            var o = JsonNode.Parse(result)!.AsObject();
            if (o["ok"]?.GetValue<bool>() != true) return (false, o["error"]?.ToString() ?? "self-test failed");
            return (true, $"reloaded and predicted {o["images"]} images ({o["objects"]} objects)");
        }
        catch (JsonException) { return (false, "self-test printed an unreadable result"); }
    }
}

/// <summary>Reads what the trial needs from the training's own output lines (D-FINE logger format, src/misc/logger.py).</summary>
public static partial class TrialLog
{
    /// <summary>Speed and memory of stage 1 from the progress lines, and the validation time per batch when it can be read.</summary>
    public sealed record Measurement(double? StepsPerSecond, double? PeakMb, double? ValSecondsPerBatch);

    // "Epoch: [0]  [ 3/40]  eta: 0:00:12  lr: …  loss: …  time: 0.3120  data: 0.0100  max mem: 5123"
    [GeneratedRegex(@"\[\s*(\d+)/(\d+)\]\s.*?\btime:\s*([0-9.]+)\s+data:\s*([0-9.]+)(?:\s+max mem:\s*([0-9.]+))?")]
    private static partial Regex Progress();

    // "Test:  Total time: 0:00:04 (0.1234 s / it)"
    [GeneratedRegex(@"^Test:\s+Total time:\s*\S+\s+\(([0-9.]+) s / it\)")]
    private static partial Regex TestTotal();

    public static Measurement Measure(IEnumerable<string> lines)
    {
        var times = new List<double>();
        double? peak = null, val = null;
        foreach (var l in lines)
        {
            if (TestTotal().Match(l) is { Success: true } t && double.TryParse(t.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tv)) val = tv;
            if (!l.Contains("Epoch:", StringComparison.Ordinal)) continue;
            var m = Progress().Match(l);
            if (!m.Success) continue;
            if (double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) times.Add(s);
            if (m.Groups[5].Success && double.TryParse(m.Groups[5].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mem))
                peak = Math.Max(peak ?? 0, mem);
        }
        double? sps = null;
        var kept = times.Skip(3).Order().ToList();
        if (kept.Count == 0) kept = times.Order().ToList();
        if (kept.Count > 0)
        {
            var median = kept.Count % 2 == 1 ? kept[kept.Count / 2] : (kept[kept.Count / 2 - 1] + kept[kept.Count / 2]) / 2;
            if (median > 0) sps = Math.Round(1 / median, 4);
        }
        return new Measurement(sps, peak, val);
    }

    /// <summary>
    /// "Load model.state_dict, {'missed': [...], 'unmatched': [...]}" → layers loaded (unknown: the line does not list them) and layers created new
    /// (missed + unmatched). No such line → (null, null).
    /// </summary>
    public static (int? Loaded, int? Created) WeightsLoad(IEnumerable<string> lines)
    {
        var line = lines.LastOrDefault(l => l.Contains("Load model.state_dict, ", StringComparison.Ordinal));
        if (line is null) return (null, null);
        return (null, Count(line, "'missed'") + Count(line, "'unmatched'"));
    }

    private static int Count(string line, string key)
    {
        var at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return 0;
        var open = line.IndexOf('[', at);
        var close = open < 0 ? -1 : line.IndexOf(']', open);
        if (open < 0 || close < 0) return 0;
        var inner = line[(open + 1)..close].Trim();
        return inner.Length == 0 ? 0 : inner.Split(',').Count(x => x.Trim().Length > 0);
    }
}
