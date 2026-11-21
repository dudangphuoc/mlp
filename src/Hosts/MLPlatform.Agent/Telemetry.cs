using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;   // Contracts.V1 also has an Environment message (catalog)

namespace MLPlatform.Agent;

/// <summary>
/// Continuous logs (20/10 §2.4): `docker logs -f -t` for each workload container → LogChunk in blocks of ≤ 64 KB or 1 s.
/// Connection lost → write to an on-disk spool (≤ 100 MB/attempt) and resend after reconnecting; the api deduplicates by source timestamp.
/// </summary>
public sealed class LogShipper(AgentState state, ILogger<LogShipper> log)
{
    private const int MaxChunkBytes = 64 * 1024;
    private const long MaxSpoolBytes = 100L * 1024 * 1024;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource> _following = new();
    private Func<Connection<ControlToAgent, AgentToControl>?> _conn = () => null;

    public void Bind(Func<Connection<ControlToAgent, AgentToControl>?> conn) => _conn = conn;

    private string SpoolDir => Directory.CreateDirectory(Path.Combine(state.Dir, "spool")).FullName;

    /// <summary>Follow a container's logs until it stops (called again when the agent re-adopts a container; `--since` from the last sent mark).</summary>
    public void Follow(string container, string attemptId, string? since = null)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_following.TryAdd(container, done)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
                foreach (var a in new[] { "logs", "-f", "-t" }) psi.ArgumentList.Add(a);
                if (since is not null) { psi.ArgumentList.Add("--since"); psi.ArgumentList.Add(since); }
                psi.ArgumentList.Add(container);
                using var p = Process.Start(psi)!;
                var lines = new List<(long, string)>();
                var bytes = 0;
                var flushAt = Environment.TickCount64 + 1000;
                var gate = new SemaphoreSlim(1);
                async Task FlushAsync()
                {
                    await gate.WaitAsync();
                    try
                    {
                        if (lines.Count == 0) return;
                        var batch = lines.ToList();
                        lines.Clear(); bytes = 0;
                        await SendOrSpoolAsync(attemptId, batch);
                    }
                    finally { gate.Release(); }
                }
                void OnLine(string? l)
                {
                    if (l is null) return;
                    // `docker logs -t`: "2026-11-18T05:03:01.123456789Z content"
                    var sp = l.IndexOf(' ');
                    var ts = sp > 0 && DateTimeOffset.TryParse(l[..sp], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
                        ? t.ToUnixTimeMilliseconds() : TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds();
                    var text = sp > 0 ? l[(sp + 1)..] : l;
                    lock (lines) { lines.Add((ts, text)); bytes += text.Length; }
                }
                p.OutputDataReceived += (_, e) => OnLine(e.Data);
                p.ErrorDataReceived += (_, e) => OnLine(e.Data);
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                while (!p.HasExited)
                {
                    await Task.Delay(200);
                    if (bytes >= MaxChunkBytes || Environment.TickCount64 >= flushAt) { await FlushAsync(); flushAt = Environment.TickCount64 + 1000; }
                }
                await Task.Delay(300);
                await FlushAsync();
            }
            catch (Exception e) when (e is IOException or InvalidOperationException) { log.LogDebug("Log follow for {C} stopped: {M}", container, e.Message); }
            finally { _following.TryRemove(container, out _); done.TrySetResult(); }
        });
    }

    /// <summary>Wait for a container's log stream to send its last block (up to <paramref name="max"/>) so logs are complete before reporting the container exit.</summary>
    public async Task DrainAsync(string container, TimeSpan max)
    {
        if (_following.TryGetValue(container, out var t)) await Task.WhenAny(t.Task, Task.Delay(max));
    }

    private async Task SendOrSpoolAsync(string attemptId, List<(long Ts, string Text)> batch)
    {
        var chunk = new LogChunk { Source = "workload", AttemptId = attemptId };
        chunk.Lines.AddRange(batch.Select(b => b.Text));
        chunk.TsMs.AddRange(batch.Select(b => b.Ts));
        if (_conn() is { } c)
        {
            try { await c.SendAsync(new AgentToControl { LogChunk = chunk }); return; }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        }
        var file = Path.Combine(SpoolDir, attemptId + ".jsonl");
        if (File.Exists(file) && new FileInfo(file).Length > MaxSpoolBytes) return;   // spool limit: drop the excess
        await File.AppendAllLinesAsync(file, batch.Select(b => new JsonObject { ["ts"] = b.Ts, ["l"] = b.Text }.ToJsonString()));
    }

    /// <summary>Reconnected: resend logs buffered on disk in order, then delete the spool.</summary>
    public async Task FlushSpoolAsync(Connection<ControlToAgent, AgentToControl> conn, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(SpoolDir, "*.jsonl"))
        {
            var attempt = Path.GetFileNameWithoutExtension(file);
            var rows = (await File.ReadAllLinesAsync(file, ct)).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
            foreach (var part in rows.Chunk(500))
            {
                var chunk = new LogChunk { Source = "workload", AttemptId = attempt };
                chunk.Lines.AddRange(part.Select(r => r["l"]!.ToString()));
                chunk.TsMs.AddRange(part.Select(r => r["ts"]!.GetValue<long>()));
                await conn.SendAsync(new AgentToControl { LogChunk = chunk }, ct);
            }
            File.Delete(file);
            log.LogInformation("Resent {N} buffered log line(s) for {Attempt}", rows.Count, attempt);
        }
    }
}

/// <summary>Host + container + GPU metrics every 15 s (20/10 §2.5): `docker stats` for platform containers, `nvidia-smi` when a GPU is present.</summary>
public static class MetricsSampler
{
    public static async Task<HostMetrics> SampleAsync(CancellationToken ct)
    {
        var m = new HostMetrics { At = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(TimeProvider.System.GetUtcNow()) };
        var (code, outp, _) = await Docker.RunAsync(["ps", "-q", "--filter", "label=mlp.managed=1"], ct);
        var ids = code == 0 ? outp.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];
        if (code != 0)
        {
            var (_, _, err) = await Docker.RunAsync(["info", "--format", "{{.ServerVersion}}"], ct, TimeSpan.FromSeconds(10));
            m.DockerDown = true;
            var line = err.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "docker is not responding";
            m.DockerError = line.Length > 240 ? line[..240] : line;
        }
        if (ids.Length > 0)
        {
            var (sc, stats, _) = await Docker.RunAsync(["stats", "--no-stream", "--format", "{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}", .. ids], ct, TimeSpan.FromSeconds(20));
            if (sc == 0)
                foreach (var line in stats.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Split('|');
                    if (p.Length < 3) continue;
                    m.Containers.Add(new ContainerSample
                    {
                        Name = p[0], State = "running",
                        CpuPercent = double.TryParse(p[1].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu) ? cpu : 0,
                        MemoryBytes = Bytes(p[2].Split('/')[0].Trim()),
                    });
                }
        }
        try
        {
            var (gc, gout, _) = await RunAsync("nvidia-smi", ["--query-gpu=uuid,utilization.gpu,memory.used,temperature.gpu,power.draw", "--format=csv,noheader,nounits"], ct);
            if (gc == 0)
                foreach (var line in gout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Split(',').Select(x => x.Trim()).ToArray();
                    if (p.Length < 5) continue;
                    double D(string x) => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
                    m.Gpus.Add(new GpuSample { Uuid = p[0], UtilPercent = D(p[1]), MemoryUsedBytes = (ulong)(D(p[2]) * 1024 * 1024), TemperatureC = D(p[3]), PowerW = D(p[4]) });
                }
        }
        catch (System.ComponentModel.Win32Exception) { }   // no nvidia-smi
        return m;
    }

    /// <summary>"12.3MiB" / "1.2GiB" / "512kB" → byte.</summary>
    public static ulong Bytes(string s)
    {
        var i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
        if (!double.TryParse(s[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
        var mul = s[i..].Trim().ToUpperInvariant() switch
        {
            "KIB" or "KB" => 1024d, "MIB" or "MB" => 1024d * 1024, "GIB" or "GB" => 1024d * 1024 * 1024, "TIB" or "TB" => 1024d * 1024 * 1024 * 1024, _ => 1d,
        };
        return (ulong)(v * mul);
    }

    private static async Task<(int, string, string)> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync(ct);
        var e = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, o, e);
    }
}
