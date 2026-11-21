using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Grpc.Core;
using Grpc.Net.Client;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;   // Contracts.V1 also has an Environment message (catalog)

namespace MLPlatform.Shim;

/// <summary>
/// Shim operations channel (20/10): standard progress (step changes sent immediately, within a step at most once per 5 s), 30 s heartbeat, two-tier checkpoints.
/// Network errors do not break training; they are only logged (unless the attempt was superseded: stop immediately so the run is not overwritten).
/// </summary>
public sealed partial class OpsReporter : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly RuntimeService.RuntimeServiceClient _client;
    private readonly Metadata _auth;
    private readonly TextWriter _log;
    private string _phase = "";
    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBeat = DateTimeOffset.MinValue;
    private (DateTimeOffset At, double Current)? _rateStart;

    /// <summary>The attempt was superseded (fencing); the shim must stop.</summary>
    public bool Fenced { get; private set; }

    private OpsReporter(string api, string token, TextWriter log)
    {
        _channel = GrpcChannel.ForAddress(api.StartsWith("http", StringComparison.Ordinal) ? api : "http://" + api);
        _client = new RuntimeService.RuntimeServiceClient(_channel);
        _auth = new Metadata { { "authorization", "Bearer " + token } };
        _log = log;
    }

    public static OpsReporter? FromEnvironment(TextWriter log)
    {
        var api = Environment.GetEnvironmentVariable("MLP_API");
        var token = Environment.GetEnvironmentVariable("MLP_JOB_TOKEN");
        return api is { Length: > 0 } && token is { Length: > 0 } ? new OpsReporter(api, token, log) : null;
    }

    private static DateTimeOffset Now => TimeProvider.System.GetUtcNow();

    public int StallAfterMin { get; set; }

    public async Task ProgressAsync(string phase, double current = 0, double total = 0, string unit = "", string message = "", double? etaSeconds = null,
        string detailJson = "", bool force = false)
    {
        var changed = phase != _phase;
        if (!changed && !force && Now - _lastSent < TimeSpan.FromSeconds(5)) return;
        if (changed) _rateStart = null;
        double percent = total > 0 ? Math.Clamp(current / total * 100, 0, 100) : 0, rate = 0, eta = 0;
        if (total > 0)
        {
            _rateStart ??= (Now, current);
            var dt = (Now - _rateStart.Value.At).TotalSeconds;
            if (dt > 1 && current > _rateStart.Value.Current)
            {
                rate = (current - _rateStart.Value.Current) / dt;
                eta = (total - current) / rate;
            }
        }
        if (etaSeconds is { } e and > 0) eta = e;
        await CallAsync(() => _client.ReportProgressAsync(new ProgressReport
        {
            Phase = phase, Current = current, Total = total, Unit = unit, Percent = percent, Rate = rate, EtaSeconds = eta, Message = message, StallAfterMin = StallAfterMin, DetailJson = detailJson,
        }, _auth, deadline: Now.UtcDateTime.AddSeconds(10)).ResponseAsync);
        _phase = phase;
        _lastSent = Now;
        _lastBeat = Now;
    }

    public async Task HeartbeatAsync()
    {
        if (Now - _lastBeat < TimeSpan.FromSeconds(30)) return;
        _lastBeat = Now;
        await CallAsync(() => _client.HeartbeatAsync(new RuntimeHeartbeat { Phase = _phase }, _auth, deadline: Now.UtcDateTime.AddSeconds(10)).ResponseAsync);
    }

    /// <summary>Checkpoint: register the local copy immediately, upload to S3 storage, then register the durable copy (only durable copies can resume on another host).</summary>
    /// <param name="epochsDone">R28: step of a checkpoint whose name has no number (last.pth) = epochs finished when it was written.</param>
    public async Task<bool> UploadCheckpointAsync(string output, string path, CancellationToken ct, long? epochsDone = null)
    {
        var name = Path.GetRelativePath(output, path).Replace('\\', '/');
        var step = HasNumber(name) ? StepOf(name) : epochsDone ?? 0;
        var size = new FileInfo(path).Length;
        string sha;
        await using (var fs = File.OpenRead(path)) sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
        await CallAsync(() => _client.RegisterCheckpointAsync(new RegisterCheckpointRequest { Name = name, Step = step, Location = "local", SizeBytes = size, Sha256 = sha },
            _auth, cancellationToken: ct).ResponseAsync);
        CheckpointUpload? up = null;
        await CallAsync(async () => up = await _client.CreateCheckpointUploadAsync(new CheckpointUploadRequest { Name = name, Step = step }, _auth, cancellationToken: ct));
        if (up is null) return false;
        using (var http = new HttpClient { Timeout = TimeSpan.FromHours(2) })
        await using (var fs = File.OpenRead(path))
        {
            try
            {
                using var res = await http.PutAsync(up.Url, new StreamContent(fs), ct);
                if (!res.IsSuccessStatusCode) { _log.WriteLine($"[mlp-shim] uploading checkpoint {name} failed with HTTP {(int)res.StatusCode}"); return false; }
            }
            catch (HttpRequestException e) { _log.WriteLine($"[mlp-shim] uploading checkpoint {name} failed: {e.Message}"); return false; }
        }
        var ok = await CallAsync(() => _client.RegisterCheckpointAsync(new RegisterCheckpointRequest
            { Name = name, Step = step, Location = "durable", Key = up.Key, SizeBytes = size, Sha256 = sha }, _auth, cancellationToken: ct).ResponseAsync);
        if (ok) _log.WriteLine($"[mlp-shim] checkpoint {name} (step {step}) saved durably");
        return ok;
    }

    // A step number stands alone in the name: "ckpt-6.json", "epoch_012.pt", "checkpoint0004.pth" — not the "1" of "best_stg1.pth".
    [GeneratedRegex(@"(?:^|[-_.])(?:epoch|ckpt|checkpoint|step)?[-_]?(\d+)\.[A-Za-z0-9]+$")] private static partial Regex StepNumber();

    public static bool HasNumber(string name) => StepNumber().IsMatch(Path.GetFileName(name));

    /// <summary>Checkpoint step = the number in the file name (ckpt-6.json → 6, epoch_012.pt → 12), 0 when there is none.</summary>
    public static long StepOf(string name) => StepNumber().Match(Path.GetFileName(name)) is { Success: true } m && long.TryParse(m.Groups[1].Value, out var n) ? n : 0;

    private async Task<bool> CallAsync(Func<Task> call)
    {
        if (Fenced) return false;
        try { await call(); return true; }
        catch (RpcException e) when (e.Trailers.GetValue("mlp-error-reason") == "SCHED_ATTEMPT_FENCED")
        {
            Fenced = true;
            _log.WriteLine("[mlp-shim] this run was superseded by a newer run; stopping so the run is not overwritten");
            return false;
        }
        catch (RpcException e) { _log.WriteLine($"[mlp-shim] sending status failed ({e.StatusCode}): {e.Status.Detail}"); return false; }
    }

    public void Dispose() => _channel.Dispose();
}

/// <summary>Find new checkpoints that have finished writing (size + mtime unchanged ≥ 10 s) using the recipe's `checkpoints` glob.</summary>
public sealed class CheckpointWatcher(string output, IReadOnlyList<string> patterns, string statePath)
{
    private readonly Dictionary<string, (long Size, DateTime Mtime, DateTimeOffset Since)> _seen = [];
    private readonly HashSet<string> _done = Load(statePath);

    public IReadOnlyList<string> Ready()
    {
        var now = TimeProvider.System.GetUtcNow();
        var ready = new List<string>();
        foreach (var f in patterns.SelectMany(p => Runner.Glob(output, p)).Where(File.Exists).Distinct())
        {
            var fi = new FileInfo(f);
            var key = $"{Path.GetRelativePath(output, f)}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            if (_done.Contains(key)) continue;
            if (!_seen.TryGetValue(f, out var s) || s.Size != fi.Length || s.Mtime != fi.LastWriteTimeUtc)
                _seen[f] = (fi.Length, fi.LastWriteTimeUtc, now);
            else if (now - s.Since >= TimeSpan.FromSeconds(10)) ready.Add(f);
        }
        return ready;
    }

    public void MarkDone(string file)
    {
        var fi = new FileInfo(file);
        _done.Add($"{Path.GetRelativePath(output, file)}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}");
        File.WriteAllText(statePath, new JsonObject { ["uploaded"] = new JsonArray(_done.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) }.ToJsonString());
    }

    private static HashSet<string> Load(string path)
    {
        try { return (JsonNode.Parse(File.ReadAllText(path))?["uploaded"] as JsonArray)?.Select(x => x!.ToString()).ToHashSet() ?? []; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return []; }
    }
}
