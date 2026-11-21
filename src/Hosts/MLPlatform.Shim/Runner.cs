using System.Diagnostics;
using System.Globalization;
using System.IO.Enumeration;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.Recipes;

namespace MLPlatform.Shim;

/// <summary>Lifecycle of one attempt (20/08 §5.1): build the command → run → collect metrics → build outputs → exit code (§12).</summary>
public sealed partial class Runner(JobSpec spec, TextWriter log)
{
    private Action<string>? _tap;   // trial: sees every line of the recipe process (stdout and stderr)
    private readonly string _mlpDir = Path.Combine(spec.Output, ".mlp");
    private string? _userError;
    private bool _oomSeen;
    private string? _recipeError;
    private OpsReporter? _ops;
    private long _maxStep = -1;
    private readonly Dictionary<string, double> _lastValue = [];
    private TrainProgress? _train;
    private List<(Regex Rx, string Reason, string Message)> _failures = [];
    private (string Reason, string Message)? _failure;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_mlpDir);
        using var ops = OpsReporter.FromEnvironment(log);
        _ops = ops;
        await using var sink = TrackingSink.FromEnvironment(log);
        try
        {
            if (spec.Trial is not null && spec.Entrypoint == "train") return await RunTrialAsync(sink, ct);
            await DownloadDataAsync(ct);
            await ConvertDataAsync(ct);
            await DownloadWeightsAsync(ct);
            await RestoreCheckpointAsync(ct);
            var entry = spec.Entry;
            var code = await ExecuteAsync(entry, spec.Entrypoint == "train" ? entry["runOverrides"] as JsonObject : null, sink, ct);
            return await CompleteAsync(code, entry, sink, ct);
        }
        finally { RemoveView(); }
    }

    /// <summary>
    /// Runs the entrypoint's command once and collects its metrics. <paramref name="extra"/> = config overrides the recipe reads from
    /// <c>MLP_EXTRA_JSON</c> (trial passes, denser progress lines) — never spliced into the command template.
    /// </summary>
    private async Task<int> ExecuteAsync(JsonObject entry, JsonObject? extra, TrackingSink? sink, CancellationToken ct)
    {
        var ops = _ops;
        var command = BuildCommand(entry);
        var workdir = entry["workdir"]?.GetValue<string>();
        var env = (entry["env"] as JsonObject)?.ToDictionary(kv => kv.Key,
            kv => Template.Render(kv.Value!.GetValue<string>(), spec.Resolve, RenderMode.Raw)) ?? [];
        File.WriteAllText(Path.Combine(_mlpDir, "command.txt"), command);
        log.WriteLine($"[mlp-shim] {spec.Entrypoint}: {command}");

        var metricsDef = entry["metrics"] as JsonObject ?? spec.Manifest["metrics"] as JsonObject ?? [];
        var collector = new MetricCollector(metricsDef, s => Template.Render(s, spec.Resolve, RenderMode.Raw));
        // R24/R25: progress from the recipe's own lines; epoch metrics land on the global step axis, values from lines at the current step.
        var train = new TrainProgress(entry["progress"] as JsonObject);
        _train = train.Enabled ? train : null;
        collector.EpochToStep = e => train.Steps > 0 ? (e + 1) * train.Steps : null;
        collector.CurrentStep = () => train.GlobalStep;
        collector.OnInvalid = (key, step) => log.WriteLine($"[mlp-shim] metric {key} is not a finite number at step {step} — not recorded");
        _failures = (spec.Manifest["failures"] as JsonArray)?.OfType<JsonObject>()
            .Where(f => f["pattern"] is not null && f["retry"]?.GetValue<bool>() != true)
            .Select(f => (new Regex(f["pattern"]!.GetValue<string>(), RegexOptions.Compiled, TimeSpan.FromMilliseconds(50)),
                f["reason"]?.GetValue<string>() ?? "recipe_failure", f["message"]?.GetValue<string>() ?? "")).ToList() ?? [];
        _failure = null;
        await using var metricsOut = new StreamWriter(Path.Combine(_mlpDir, "metrics.jsonl"), append: true);

        var psi = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = workdir is not null && Directory.Exists(workdir) ? workdir : spec.Output,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
        foreach (var (k, v) in env) psi.Environment[k] = v;
        psi.Environment["MLP_OUTPUT"] = spec.Output;
        if (extra is { Count: > 0 }) psi.Environment["MLP_EXTRA_JSON"] = extra.ToJsonString();

        using var proc = Process.Start(psi) ?? throw new ShimException(3, "process_start_failed", "Could not start the process");
        proc.OutputDataReceived += (_, e) => OnLine("stdout", e.Data, collector);
        proc.ErrorDataReceived += (_, e) => OnLine("stderr", e.Data, collector);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // SIGTERM from the agent → forward to the child process, wait 60 s (20/08 §5.1 step 8)
        using var reg = ct.Register(() =>
        {
            try { Process.Start("kill", ["-TERM", proc.Id.ToString()])?.WaitForExit(2000); } catch { /* already exited */ }
            if (!proc.WaitForExit(60_000)) proc.Kill(entireProcessTree: true);
        });

        // `running` step progress follows the recipe's `progress` declaration (20/10 §2.2): step/epoch metric → current = highest step + 1.
        var progressDef = entry["progress"] as JsonObject;
        var progressMetric = progressDef?["metric"]?.GetValue<string>();
        double progressTotal = 0;
        if (progressDef?["total"] is { } tot)
            double.TryParse(Template.Render(tot.ToString(), spec.Resolve, RenderMode.Raw), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out progressTotal);
        var unit = progressDef?["unit"]?.GetValue<string>() ?? progressMetric ?? "";
        if (ops is not null && progressDef?["stallAfterMin"] is JsonValue sa && sa.TryGetValue<int>(out var stall)) ops.StallAfterMin = stall;
        var ckGlobs = CheckpointGlobs(entry);
        CheckpointWatcher? watcher = spec.Entrypoint == "train" && ckGlobs.Count > 0 && ops is not null
            ? new CheckpointWatcher(spec.Output, ckGlobs, Path.Combine(_mlpDir, "state.json"))
            : null;
        var phases = spec.Manifest["canvas"]?["phases"] as JsonArray;
        var configPath = (entry["progress"] as JsonObject)?["config"]?.GetValue<string>() is { } cfgT ? Template.Render(cfgT, spec.Resolve, RenderMode.Raw) : null;
        if (ops is not null) await ops.ProgressAsync("running", 0, progressTotal, unit, spec.Checkpoint is null ? "" : "resuming from checkpoint");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var wait = proc.WaitForExitAsync(CancellationToken.None);
        while (!wait.IsCompleted)
        {
            await Task.WhenAny(wait, timer.WaitForNextTickAsync().AsTask());
            await Flush(collector, metricsOut, sink);
            if (ops is null) continue;
            if (train.Known)
            {
                train.Stages ??= TrainProgress.ReadStages(phases, configPath, train.Epochs);
                await ops.ProgressAsync("running", train.GlobalStep, train.Epochs * train.Steps, "step", "", train.EtaSeconds(), train.Detail().ToJsonString());
            }
            else
            {
                double current = progressMetric is "step" or "epoch" ? _maxStep + 1 : progressMetric is not null && _lastValue.TryGetValue(progressMetric, out var v) ? v : 0;
                await ops.ProgressAsync("running", Math.Max(0, current), progressTotal, unit);
            }
            await ops.HeartbeatAsync();
            if (watcher is not null)
                foreach (var f in watcher.Ready())
                    if (await ops.UploadCheckpointAsync(spec.Output, f, ct, train.Known ? train.CompletedEpochs : null)) watcher.MarkDone(f);
            if (ops.Fenced)
            {
                try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new ShimException(1, "attempt_fenced", "This run was superseded by a newer run");
            }
        }
        await Flush(collector, metricsOut, sink);

        var code = proc.ExitCode;
        log.WriteLine($"[mlp-shim] process exited {code}");
        return code;
    }

    /// <summary>Exit code of the recipe process → model folder and upload (train), export list, or the failure class (20/08 §12).</summary>
    private async Task<int> CompleteAsync(int code, JsonObject entry, TrackingSink? sink, CancellationToken ct)
    {
        var ops = _ops;
        if (code == 0)
        {
            if (spec.Entrypoint == "train")
            {
                if (ops is not null) await ops.ProgressAsync("uploading", 0, 0, "", "model folder");
                BuildModelFolder();
                if (sink is not null)
                {
                    await UploadWhenReachableAsync(() => sink.UploadModelFolderAsync(Path.Combine(spec.Output, "model"), ct), ops, ct);
                }
            }
            if (spec.Entrypoint == "export") RecordExportOutputs(entry);
            if (ops is not null) await ops.ProgressAsync("finishing", 1, 1);
            return 0;
        }
        if (_failure is { } nf)
        {
            // R26: a declared failure (e.g. loss became NaN) — retrying the same attempt cannot fix it. MLP_FAIL carries the reason to the control plane.
            log.WriteLine("MLP_FAIL " + new JsonObject { ["reason"] = nf.Reason, ["message"] = nf.Message }.ToJsonString());
            return Fail(2, nf.Reason, nf.Message);
        }
        if (code == 137 || _oomSeen) return Fail(137, "oom", "Out of memory (OOM); try a smaller batch");
        if (code == 2 || _userError is not null) return Fail(2, "user_error", _userError ?? "User error");
        if (code is 126 or 127) return Fail(3, "recipe_error", "The recipe command could not run (not found or not permitted)");
        if (_recipeError is not null) return Fail(3, "recipe_error", "The recipe image is missing a dependency: " + _recipeError);
        return code;   // anything else → infrastructure error, the scheduler retries
    }

    /// <summary>
    /// Fleet autonomy (D-40): training finished but the control plane or storage is unreachable → keep the result on the run volume
    /// and retry with backoff (up to MLP_UPLOAD_WAIT_HOURS, default 24) instead of failing a finished run.
    /// </summary>
    private async Task UploadWhenReachableAsync(Func<Task> upload, OpsReporter? ops, CancellationToken ct)
    {
        var limit = TimeSpan.FromHours(double.TryParse(Environment.GetEnvironmentVariable("MLP_UPLOAD_WAIT_HOURS"), System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0 ? h : 24);
        var started = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            string why;
            try { await upload(); return; }
            catch (Grpc.Core.RpcException e) when (e.StatusCode is Grpc.Core.StatusCode.Unavailable or Grpc.Core.StatusCode.DeadlineExceeded or Grpc.Core.StatusCode.Internal or Grpc.Core.StatusCode.Unknown)
            { why = $"control plane unreachable ({e.StatusCode})"; }
            catch (Grpc.Core.RpcException e) { throw new ShimException(1, "artifact_upload_failed", e.Status.Detail); }
            catch (HttpRequestException e) { why = "storage unreachable: " + e.Message; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { why = "upload timed out"; }
            if (started.Elapsed > limit) throw new ShimException(1, "artifact_upload_failed", $"Could not upload the model for {limit.TotalHours:0.#} hours: {why}");
            var wait = TimeSpan.FromSeconds(Math.Min(60, 5 * attempt));
            log.WriteLine($"[mlp-shim] model upload waiting ({why}); the result is kept on the run volume, retry {attempt} in {wait.TotalSeconds:0} s");
            if (ops is not null) await ops.ProgressAsync("uploading", 0, 0, "", "waiting for the control plane");
            await Task.Delay(wait, ct);
        }
    }

    /// <summary>
    /// Download the dataset version's files (20/08 §5.1 step 2), 4 at a time. Large lists come from <c>dataFilesUrl</c> (an env var holds at most 128 KB).
    /// Files land on the run's volume (<c>{output}/.mlp/data</c>), so a retried attempt on the same machine only fetches what is missing.
    /// HTTP 429/503 (e.g. Hugging Face rate limits) pauses every download until Retry-After, then backs off. Failure → code 1 (the scheduler retries).
    /// </summary>
    private async Task DownloadDataAsync(CancellationToken ct)
    {
        if (spec.Data is null) return;
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        var list = spec.DataFiles;
        if (list.Count == 0 && spec.DataFilesUrl is { } listUrl)
        {
            try { list = JsonNode.Parse(await http.GetStringAsync(listUrl, ct)) as JsonArray ?? []; }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
            {
                throw new ShimException(1, "data_download_failed", $"Could not read the dataset file list: {e.Message}");
            }
        }
        if (list.Count == 0) return;
        if (spec.DataCache is { } cache && Directory.Exists(cache))
        {
            await SyncCachedAsync(cache, list.OfType<JsonObject>().Select(f => new SyncFile(f["path"]!.GetValue<string>(), f["url"]!.GetValue<string>(),
                f["size"] is JsonValue sv && sv.TryGetValue<long>(out var size) ? size : -1, f["id"]?.GetValue<string>())).ToList(), ct);
            return;
        }
        if (Directory.Exists(spec.Output)) spec.Data = Path.Combine(_mlpDir, "data");   // cache on the run volume (kept across attempts)
        var files = list.OfType<JsonObject>().Select(f => (Path: f["path"]!.GetValue<string>(), Url: f["url"]!.GetValue<string>())).ToList();
        var root = Path.GetFullPath(spec.Data);
        long total = 0;
        var done = 0;
        var cached = 0;
        long pauseUntil = 0;   // Environment.TickCount64 before which nobody sends a request (shared rate-limit pause)
        using var progress = new CancellationTokenSource();
        var reporter = Task.Run(async () =>
        {
            while (!progress.IsCancellationRequested && _ops is not null)
            {
                var wait = Interlocked.Read(ref pauseUntil) - Environment.TickCount64;
                await _ops.ProgressAsync("preparing_data", Volatile.Read(ref done), files.Count, "file",
                    wait > 0 ? $"rate limited by the source — waiting {wait / 1000} s" : "downloading dataset");
                try { await Task.Delay(3000, progress.Token); } catch (OperationCanceledException) { break; }
            }
        }, CancellationToken.None);
        try
        {
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (f, token) =>
            {
                var target = Path.GetFullPath(Path.Combine(root, f.Path));
                if (!target.StartsWith(root, StringComparison.Ordinal))
                    throw new ShimException(2, "data_path_invalid", $"File path is outside the data folder: {f.Path}");
                if (File.Exists(target)) { Interlocked.Increment(ref cached); Interlocked.Increment(ref done); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                for (var attempt = 1; ; attempt++)
                {
                    var wait = Interlocked.Read(ref pauseUntil) - Environment.TickCount64;
                    if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), token);
                    TimeSpan? backoff = null;
                    try
                    {
                        using var res = await http.GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead, token);
                        if ((int)res.StatusCode is 429 or 503)
                        {
                            backoff = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(300, 15 * Math.Pow(2, Math.Min(attempt, 5) - 1)));
                            var until = Environment.TickCount64 + (long)backoff.Value.TotalMilliseconds;
                            long seen;
                            do { seen = Interlocked.Read(ref pauseUntil); } while (until > seen && Interlocked.CompareExchange(ref pauseUntil, until, seen) != seen);
                            throw new HttpRequestException($"HTTP {(int)res.StatusCode} (rate limited)");
                        }
                        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode}");
                        await using (var fs = File.Create(target + ".part"))
                        {
                            await res.Content.CopyToAsync(fs, token);
                            Interlocked.Add(ref total, fs.Length);
                        }
                        File.Move(target + ".part", target, overwrite: true);
                        break;
                    }
                    catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !token.IsCancellationRequested)
                    {
                        if (attempt >= 10) throw new ShimException(1, "data_download_failed", $"Downloading {f.Path} failed: {e.Message}");
                        if (backoff is null) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), token);
                    }
                }
                Interlocked.Increment(ref done);
            });
        }
        finally
        {
            await progress.CancelAsync();
            await reporter;
        }
        log.WriteLine($"[mlp-shim] dataset ready in {spec.Data}: {files.Count} file(s), {cached} already cached, {total} bytes downloaded");
    }

    /// <summary>Pretrained variant weights (fine-tuning): downloaded to {weights} when the file is not there yet.</summary>
    private async Task DownloadWeightsAsync(CancellationToken ct)
    {
        if (spec.Weights is not { } path || spec.WeightsUrl is not { } url || File.Exists(path)) return;
        if (_ops is not null) await _ops.ProgressAsync("preparing_data", 0, 1, "", "downloading pretrained weights");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        try
        {
            using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) throw new ShimException(1, "weights_download_failed", $"Downloading pretrained weights failed with HTTP {(int)res.StatusCode}");
            await using (var fs = File.Create(path + ".part")) await res.Content.CopyToAsync(fs, ct);
            File.Move(path + ".part", path, overwrite: true);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
        {
            throw new ShimException(1, "weights_download_failed", $"Downloading pretrained weights failed: {e.Message}");
        }
        log.WriteLine($"[mlp-shim] pretrained weights: {url} → {path}");
    }

    /// <summary>
    /// The recipe reads COCO (<c>data.input: coco</c>) → run the platform converter baked into the image (YOLO / COCO tree → COCO {train,val}).
    /// It links images instead of copying them and writes annotations with class ids 0..N-1 in the dataset's class order.
    /// </summary>
    private async Task ConvertDataAsync(CancellationToken ct)
    {
        const string converter = "/opt/mlp-convert/to_coco.py";
        if (spec.Data is not { } data || !Directory.Exists(data) || spec.Entrypoint != "train"
            || spec.Manifest["data"]?["input"]?.GetValue<string>() != "coco" || !File.Exists(converter)) return;
        if (_ops is not null) await _ops.ProgressAsync("preparing_data", 0, 1, "", "converting the dataset to COCO");
        // On the run volume, never next to the data: the data may be a view inside the shared dataset cache (DataSync.cs).
        var dst = Path.Combine(_mlpDir, "data-coco");
        var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-I", converter, "--src", data, "--dst", dst, "--classes", spec.Dataset["classes.names_json"]?.GetValue<string>() ?? "",
                     "--seed", spec.Params["seed"]?.ToString() ?? "0" })
            psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new ShimException(3, "converter_start_failed", "Could not start the dataset converter");
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) { log.WriteLine(e.Data); CheckUserError(e.Data); } };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
            throw new ShimException(2, "data_convert_failed", _userError ?? $"Dataset conversion to COCO failed (exit {proc.ExitCode})");
        spec.Data = dst;
    }

    /// <summary>Checkpoint globs of the entrypoint, relative to {output}: `checkpoints` is one path or a list (R28: last.pth + best_stg1.pth).</summary>
    private List<string> CheckpointGlobs(JsonObject entry) => (entry["checkpoints"] switch
    {
        JsonArray a => a.Select(x => x?.GetValue<string>()).OfType<string>(),
        JsonValue v when v.TryGetValue<string>(out var one) => [one],
        _ => [],
    }).Select(g => Path.GetRelativePath(spec.Output, Path.Combine(spec.Output, Template.Render(g, spec.Resolve, RenderMode.Raw)))).ToList();

    /// <summary>
    /// `MLP_RESUME_FROM` (20/10 §4.6), one entry per line: `local:&lt;name&gt;` = checkpoint still on the run's volume (same host);
    /// `url:&lt;presigned&gt;|&lt;name&gt;|&lt;sha256&gt;` = durable copy in storage, downloaded to the right place and checked against its sha.
    /// Several names (R28: last.pth and best_stg1.pth) are all restored; the one resumed from is the first in the recipe's `checkpoints` order.
    /// </summary>
    private async Task RestoreCheckpointAsync(CancellationToken ct)
    {
        var from = Environment.GetEnvironmentVariable("MLP_RESUME_FROM");
        if (string.IsNullOrEmpty(from) || spec.Checkpoint is not null) return;
        var entries = from.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_ops is not null) await _ops.ProgressAsync("restoring_checkpoint", 0, entries.Length, "file", entries[0].StartsWith("local:", StringComparison.Ordinal) ? "local copy" : "downloading durable copy");
        var root = Path.GetFullPath(spec.Output);
        var restored = new List<string>();
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        foreach (var e in entries)
        {
            if (e.StartsWith("local:", StringComparison.Ordinal))
            {
                var p = Path.GetFullPath(Path.Combine(spec.Output, e[6..]));
                if (p.StartsWith(root, StringComparison.Ordinal) && File.Exists(p)) restored.Add(p);
                else log.WriteLine($"[mlp-shim] local checkpoint {e[6..]} not found");
                continue;
            }
            if (!e.StartsWith("url:", StringComparison.Ordinal)) continue;
            var parts = e[4..].Split('|');
            if (parts.Length < 2) continue;
            var (url, name, sha) = (parts[0], parts[1], parts.Length > 2 ? parts[2] : "");
            var target = Path.GetFullPath(Path.Combine(spec.Output, name));
            if (!target.StartsWith(root, StringComparison.Ordinal)) throw new ShimException(1, "checkpoint_path_invalid", name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!res.IsSuccessStatusCode) throw new ShimException(1, "checkpoint_download_failed", $"{name}: HTTP {(int)res.StatusCode}");
                await using (var fs = File.Create(target + ".part")) await res.Content.CopyToAsync(fs, ct);
            }
            catch (HttpRequestException ex) { throw new ShimException(1, "checkpoint_download_failed", $"{name}: {ex.Message}"); }
            if (sha.Length > 0)
            {
                string got;
                await using (var fs = File.OpenRead(target + ".part")) got = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(fs, ct));
                if (got != sha) throw new ShimException(1, "checkpoint_corrupt", $"{name}: sha256 {got[..12]}… does not match the recorded {sha[..Math.Min(12, sha.Length)]}…");
                log.WriteLine($"[mlp-shim] checkpoint {name}: sha256 verified");
            }
            File.Move(target + ".part", target, overwrite: true);
            restored.Add(target);
        }
        if (restored.Count == 0) { log.WriteLine("[mlp-shim] no checkpoint could be restored; starting from scratch"); return; }
        var order = CheckpointGlobs(spec.Entry);
        var primary = order.SelectMany(g => restored.Where(r => FileSystemName.MatchesSimpleExpression(g, Path.GetRelativePath(spec.Output, r)))).FirstOrDefault() ?? restored[0];
        spec.Checkpoint = primary;
        log.WriteLine($"[mlp-shim] resuming from checkpoint {Path.GetRelativePath(spec.Output, primary)}"
            + (restored.Count > 1 ? $" (also restored: {string.Join(", ", restored.Where(r => r != primary).Select(r => Path.GetRelativePath(spec.Output, r)))})" : ""));
    }

    private int Fail(int code, string reason, string message)
    {
        File.WriteAllText(Path.Combine(_mlpDir, "exit.json"),
            new JsonObject { ["code"] = code, ["reason"] = reason, ["message"] = message }.ToJsonString());
        return code;
    }

    private void OnLine(string stream, string? line, MetricCollector collector)
    {
        if (line is null) return;
        (stream == "stdout" ? Console.Out : Console.Error).WriteLine(line);
        _tap?.Invoke(line);
        _train?.OnLine(line);
        CheckUserError(line);
        if (_failure is null)
            foreach (var (rx, reason, message) in _failures)
            {
                bool hit;
                try { hit = rx.IsMatch(line); } catch (RegexMatchTimeoutException) { continue; }
                if (hit) { _failure = (reason, message.Length > 0 ? message : line.Trim()); break; }
            }
        if (line.Contains("CUDA out of memory", StringComparison.Ordinal) || line.Contains("OutOfMemoryError", StringComparison.Ordinal))
            _oomSeen = true;
        // A missing Python package is a broken recipe image, not a flaky machine: retrying the attempt cannot fix it.
        if (_recipeError is null)
            foreach (var marker in new[] { "ModuleNotFoundError: ", "ImportError: " })
                if (line.IndexOf(marker, StringComparison.Ordinal) is >= 0 and var at) { _recipeError = line[at..].Trim(); break; }
        collector.OnLine(stream, line);
    }

    /// <summary>`MLP_ERROR {json}` on a line = user error (20/08 §12): its message becomes the run's failure reason.</summary>
    private void CheckUserError(string line)
    {
        if (!line.StartsWith("MLP_ERROR ", StringComparison.Ordinal)) return;
        try
        {
            var o = JsonNode.Parse(line[10..]);
            _userError = o?["message"]?.GetValue<string>() ?? o?["reason"]?.GetValue<string>() ?? line[10..];
        }
        catch (JsonException) { _userError = line[10..]; }
    }

    private async Task Flush(MetricCollector c, StreamWriter w, TrackingSink? sink)
    {
        var pts = c.Poll();
        foreach (var p in pts) { _maxStep = Math.Max(_maxStep, p.Step); _lastValue[p.Key] = p.Value; }
        foreach (var p in pts)
            await w.WriteLineAsync(new JsonObject { ["key"] = p.Key, ["step"] = p.Step, ["value"] = p.Value }.ToJsonString());
        await w.FlushAsync();
        if (sink is not null) await sink.SendAsync(pts);
    }

    /// <summary>Build the command; with a checkpoint → apply `resume` (append | replace | command) (20/08 §5.2).</summary>
    public string BuildCommand(JsonObject entry)
    {
        var raw = entry["command"]?.GetValue<string>()
                  ?? throw new ShimException(3, "recipe_command_missing", $"Entrypoint {spec.Entrypoint} has no command");
        if (spec.Checkpoint is not null && entry["resume"] is JsonObject r)
        {
            switch (r["mode"]?.GetValue<string>())
            {
                case "append": raw = raw.TrimEnd() + " " + r["args"]!.GetValue<string>(); break;
                case "replace":
                    var from = r["from"]!.GetValue<string>();
                    if (!raw.Contains(from, StringComparison.Ordinal))
                        throw new ShimException(3, "resume_from_not_found", $"resume.from is not in the command: {from}");
                    raw = raw.Replace(from, r["to"]!.GetValue<string>(), StringComparison.Ordinal);
                    break;
                case "command": raw = r["command"]!.GetValue<string>(); break;
            }
        }
        try
        {
            return Template.Render(raw.Replace('\n', ' ').Trim(), spec.Resolve);
        }
        catch (TemplateException e)
        {
            throw new ShimException(2, e.Reason, e.Message);
        }
    }

    /// <summary>Standard D-28 model folder at {output}/model (20/08 §8).</summary>
    private void BuildModelFolder()
    {
        var art = spec.Manifest["artifacts"] as JsonObject ?? throw new ShimException(3, "recipe_artifacts_missing", "Missing artifacts");
        var best = art["best"]!.AsObject();
        var asName = best["as"]!.GetValue<string>();
        var model = Path.Combine(spec.Output, "model");
        Directory.CreateDirectory(Path.Combine(model, "weights"));
        Directory.CreateDirectory(Path.Combine(model, "config"));

        var exclude = new List<string> { "model", ".mlp" };
        exclude.AddRange(CheckpointGlobs(spec.Entry));
        var found = false;
        foreach (var g in best["from"]!.AsArray().Select(x => x!.GetValue<string>()))
        {
            var match = Glob(spec.Output, g).FirstOrDefault();
            if (match is null) continue;
            var target = Path.Combine(model, "weights", asName);
            if (Directory.Exists(match)) CopyDir(match, target, spec.Output, exclude);
            else File.Copy(match, target, overwrite: true);
            found = true;
            break;
        }
        if (!found) throw new ShimException(3, "artifact_missing", $"Best file not found ({string.Join(", ", best["from"]!.AsArray())})");

        foreach (var f in (art["files"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var src = Path.Combine(spec.Output, f["from"]!.GetValue<string>());
            var dst = Path.Combine(model, f["to"]!.GetValue<string>());
            if (!File.Exists(src)) { log.WriteLine($"[mlp-shim] skipping missing file: {f["from"]}"); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
        if (spec.Dataset["classes.json_path"]?.GetValue<string>() is { } cj && File.Exists(cj))
            File.Copy(cj, Path.Combine(model, "classes.json"), overwrite: true);
        else if (spec.Dataset["classes.names_json"]?.GetValue<string>() is { Length: > 2 } names)
            File.WriteAllText(Path.Combine(model, "classes.json"), names);   // class names by id, as the model was trained
        File.WriteAllText(Path.Combine(model, "signature.json"), new JsonObject { ["task"] = spec.Task }.ToJsonString());

        var lockDoc = new JsonObject
        {
            ["recipe"] = spec.Manifest["metadata"]?["name"]?.DeepClone(),
            ["source"] = spec.Manifest["source"]?.DeepClone(),
            ["variant"] = spec.Variant["name"]?.DeepClone(),
            ["task"] = spec.Task,
            ["params"] = spec.Params.DeepClone(),
            ["weights"] = new JsonObject { ["ref"] = spec.WeightsRef },
        };
        if (art["baseWeights"]?.GetValue<string>() is { } bw)
        {
            var baseRef = Template.Render(bw, spec.Resolve, RenderMode.Raw);
            if (baseRef.Length > 0) lockDoc["baseWeights"] = new JsonObject { ["ref"] = baseRef };
        }
        File.WriteAllText(Path.Combine(model, "recipe.lock.json"), lockDoc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        log.WriteLine($"[mlp-shim] model folder: {model}");
    }

    private void RecordExportOutputs(JsonObject entry)
    {
        var globs = (entry["outputs"] as JsonArray)?.Select(x => Template.Render(x!.GetValue<string>(), spec.Resolve, RenderMode.Raw)).ToList() ?? ["*"];
        var files = globs.SelectMany(g => Glob(spec.Output, g))
            .Where(p => !p.StartsWith(_mlpDir, StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(spec.Output, p)).Distinct().ToList();
        if (files.Count == 0) throw new ShimException(3, "export_output_missing", "Export produced no files matching outputs");
        File.WriteAllText(Path.Combine(_mlpDir, "export-files.json"), new JsonArray(files.Select(f => (JsonNode)f!).ToArray()).ToJsonString());
    }

    internal static IEnumerable<string> Glob(string root, string pattern)
    {
        if (pattern is "." or "./") return [root];
        var full = Path.Combine(root, pattern);
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return File.Exists(full) || Directory.Exists(full) ? [full] : [];
        var dir = Path.GetDirectoryName(full)!;
        var namePat = Path.GetFileName(full);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFileSystemEntries(dir).Where(p => FileSystemName.MatchesSimpleExpression(namePat, Path.GetFileName(p))).Order();
    }

    private static void CopyDir(string src, string dst, string outputRoot, List<string> excludeRel)
    {
        Directory.CreateDirectory(dst);
        foreach (var entry in Directory.EnumerateFileSystemEntries(src))
        {
            var rel = Path.GetRelativePath(outputRoot, entry);
            if (excludeRel.Any(x => FileSystemName.MatchesSimpleExpression(x, rel)) || Path.GetFileName(entry) is "runs" or "summary") continue;
            var target = Path.Combine(dst, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyDir(entry, target, outputRoot, excludeRel);
            else File.Copy(entry, target, overwrite: true);
        }
    }
}

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
