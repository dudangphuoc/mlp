using System.Collections.Concurrent;
using System.Text;
using MLPlatform.ControlPlane.Application.Ops;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.Api.Infrastructure;

/// <summary>
/// Per-attempt log store (20/10 §2.4): keeps the latest 2,000 lines in memory for realtime viewing; writes everything to S3 storage in ~1 MB chunks
/// (`mlp-artifacts/{ws}/jobs/{job}/attempts/{attempt}/logs/{seq}.log`, line = "ts_ms\tcontent"). Deduplicated by source timestamp (resent after reconnect).
/// </summary>
public sealed class LogStore(IServiceScopeFactory scopes, S3Client s3, StreamBroker broker, ILogger<LogStore> log)
{
    private sealed class Buf
    {
        public readonly LinkedList<(long Ts, string Text)> Tail = new();
        public readonly StringBuilder Pending = new();
        public readonly List<string> Trial = [];   // "[mlp-trial]" lines of the attempt, kept whole (the tail drops old lines)
        public long LastTs = long.MinValue;
        public int Seq;
        public Guid JobId, WorkspaceId;
        public bool Resolved;
    }

    private readonly ConcurrentDictionary<Guid, Buf> _bufs = new();

    public async Task AppendAsync(Guid attempt, IReadOnlyList<long> ts, IReadOnlyList<string> lines)
    {
        var b = _bufs.GetOrAdd(attempt, _ => new Buf());
        if (!b.Resolved)
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new AttemptOwnerQuery(attempt)) is not { } o) return;
            (b.JobId, b.WorkspaceId, b.Resolved) = (o.JobId, o.WorkspaceId, true);
        }
        string? flush = null;
        lock (b)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var t = i < ts.Count ? ts[i] : 0;
                if (t != 0 && t < b.LastTs) continue;          // already have it (duplicate resend)
                if (t == b.LastTs && b.Tail.Last?.Value.Text == lines[i]) continue;
                b.LastTs = Math.Max(b.LastTs, t);
                b.Tail.AddLast((t, lines[i]));
                if (lines[i].Contains("[mlp-trial] ", StringComparison.Ordinal) && b.Trial.Count < 1000) b.Trial.Add(lines[i]);
                if (b.Tail.Count > 2000) b.Tail.RemoveFirst();
                b.Pending.Append(t).Append('\t').Append(lines[i].Replace('\n', ' ')).Append('\n');
            }
            if (b.Pending.Length >= 1024 * 1024) { flush = b.Pending.ToString(); b.Pending.Clear(); b.Seq++; }
        }
        broker.Pulse(StreamBroker.Attempt(attempt));
        if (flush is not null) await PersistAsync(attempt, b, b.Seq, flush);
    }

    public IReadOnlyList<(long Ts, string Text)> Tail(Guid attempt, long afterTs, int max)
    {
        if (!_bufs.TryGetValue(attempt, out var b)) return [];
        lock (b) return b.Tail.Where(x => x.Ts > afterTs).TakeLast(max).ToList();
    }

    /// <summary>
    /// The trial lines of an attempt: from memory, or — after an api restart — from the stored log. Each line is a whole <c>[mlp-trial] {json}</c>.
    /// </summary>
    public async Task<IReadOnlyList<string>> TrialLinesAsync(Guid workspace, Guid job, Guid attempt, CancellationToken ct)
    {
        if (_bufs.TryGetValue(attempt, out var b))
            lock (b) if (b.Trial.Count > 0) return b.Trial.ToList();
        return (await ReadPersistedAsync(workspace, job, attempt, 20_000, ct)).Select(l => l.Text)
            .Where(l => l.Contains("[mlp-trial] ", StringComparison.Ordinal)).ToList();
    }

    /// <summary>Read the last <paramref name="max"/> lines from storage (memory only keeps the newest ones, and nothing after an api restart).</summary>
    public async Task<IReadOnlyList<(long Ts, string Text)>> ReadPersistedAsync(Guid workspace, Guid job, Guid attempt, int max, CancellationToken ct)
    {
        var prefix = $"{workspace}/jobs/{job}/attempts/{attempt}/logs/";
        var chunks = new List<List<(long, string)>>();
        var count = 0;
        // Newest chunk first, until enough lines are collected; 400 chunks bounds the work for a very long job.
        foreach (var o in (await s3.ListAsync(MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts, prefix, ct)).OrderByDescending(o => o.Key).Take(400))
        {
            var lines = new List<(long, string)>();
            foreach (var line in Encoding.UTF8.GetString(await s3.GetAsync(MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts, o.Key, ct) ?? []).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                if (line.IndexOf('\t') is var tab and > 0 && long.TryParse(line[..tab], out var t)) lines.Add((t, line[(tab + 1)..]));
            chunks.Add(lines);
            count += lines.Count;
            if (count >= max) break;
        }
        chunks.Reverse();
        return chunks.SelectMany(c => c).TakeLast(max).ToList();
    }

    /// <summary>Write what is still buffered (periodically, and when the api shuts down).</summary>
    public async Task FlushAllAsync()
    {
        foreach (var attempt in _bufs.Keys) await FlushAsync(attempt);
    }

    /// <summary>Write one attempt's buffered lines now (its container exited: the log must be complete in storage, not only in memory).</summary>
    public async Task FlushAsync(Guid attempt)
    {
        if (!_bufs.TryGetValue(attempt, out var b)) return;
        string? data = null;
        lock (b) if (b.Pending.Length > 0) { data = b.Pending.ToString(); b.Pending.Clear(); b.Seq++; }
        if (data is not null) await PersistAsync(attempt, b, b.Seq, data);
    }

    private async Task PersistAsync(Guid attempt, Buf b, int seq, string data)
    {
        // The chunk name is its write time, not a counter: a counter restarts at 0 with the api and would overwrite the chunks of an attempt
        // that is still running. Names sort in write order, after the counter-named chunks of older attempts.
        var key = $"{b.WorkspaceId}/jobs/{b.JobId}/attempts/{attempt}/logs/{TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds():D15}-{seq:D4}.log";
        try
        {
            await s3.PutAsync(MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts, key, Encoding.UTF8.GetBytes(data), "text/plain; charset=utf-8", CancellationToken.None);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or S3Exception or TaskCanceledException)
        {
            // Storage refused or is unreachable: keep the lines for the next flush instead of losing them.
            lock (b) b.Pending.Insert(0, data);
            log.LogWarning("Saving log {Attempt} to storage failed (kept for the next flush): {M}", attempt, e.Message);
        }
    }
}

/// <summary>
/// System health (20/10 §2.6): components (PostgreSQL, S3 storage, Ray head, agent channel) checked every 15 s; each background loop records its own heartbeat,
/// missing for more than 3x its period → `down` + a `system.loop_stalled` event.
/// </summary>
public sealed class SystemHealth(TimeProvider time)
{
    public sealed record Item(string Name, string Group, string Status, string Message, DateTimeOffset? LastOkAt, DateTimeOffset CheckedAt, double PeriodSeconds);

    private readonly ConcurrentDictionary<string, Item> _items = new();
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["scheduler"] = "Job scheduling", ["deployment-prober"] = "Deployment probing", ["notebook-reaper"] = "Idle notebook cleanup",
        ["sweep"] = "Sweep orchestration", ["pipeline"] = "Pipeline engine", ["monitor"] = "Drift monitoring", ["feature"] = "Materialize feature",
        ["recovery"] = "Unfinished work recovery", ["email"] = "Email delivery", ["overlay"] = "WireGuard overlay sync", ["agent-upgrade"] = "Agent update (OTA)", ["hub-import"] = "Hugging Face import", ["postgres"] = "Database", ["s3"] = "S3 storage", ["ray"] = "Ray head", ["agent-channel"] = "Agent channel :7443", ["stream-broker"] = "Realtime stream (LISTEN/NOTIFY)",
    };
    public static string Label(string n) => Labels.TryGetValue(n, out var l) ? l : n;

    /// <summary>Called by a background loop on each pass. periodSeconds = 0: event-driven worker (queue); long idle periods are not an incident.</summary>
    public void Beat(string loop, double periodSeconds) =>
        _items[loop] = new Item(loop, "loop", "ok", "Running", time.GetUtcNow(), time.GetUtcNow(), periodSeconds);

    public void Component(string name, bool ok, string message) =>
        _items.AddOrUpdate(name, _ => new Item(name, "component", ok ? "ok" : "down", message, ok ? time.GetUtcNow() : null, time.GetUtcNow(), 15),
            (_, old) => new Item(name, "component", ok ? "ok" : "down", message, ok ? time.GetUtcNow() : old.LastOkAt, time.GetUtcNow(), 15));

    /// <summary>Snapshot: a background loop with no heartbeat for more than 3 periods → down.</summary>
    public IReadOnlyList<Item> Snapshot()
    {
        var now = time.GetUtcNow();
        return _items.Values.Select(i => i.Group == "loop" && i.PeriodSeconds > 0 && i.LastOkAt is { } t && (now - t).TotalSeconds > 3 * i.PeriodSeconds + 5
            ? i with { Status = "down", Message = $"Not running for {(now - t).TotalSeconds:0} seconds" } : i).OrderBy(i => i.Group).ThenBy(i => i.Name).ToList();
    }
}

/// <summary>Check components every 15 s; record an event when an item goes down (incident) or recovers.</summary>
public sealed class HealthLoop(SystemHealth health, IServiceScopeFactory scopes, S3Client s3, LogStore logs, IHttpClientFactory http, IConfiguration cfg, ILogger<HealthLoop> log) : BackgroundService
{
    private readonly Dictionary<string, string> _last = [];
    private int _tick;

    /// <summary>Shutdown: lines still in memory go to storage first — otherwise every deploy would cut up to a minute out of each running log.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await logs.FlushAllAsync();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                if (++_tick % 4 == 0) await logs.FlushAllAsync();   // buffered logs → S3 storage every 60 s
                await using var scope = scopes.CreateAsyncScope();
                var m = scope.ServiceProvider.GetRequiredService<IMediator>();
                try { await m.Query(new DbPingQuery(), stop); health.Component("postgres", true, "Operational"); }
                catch (Exception e) when (e is not OperationCanceledException) { health.Component("postgres", false, "Cannot connect; all writes are paused"); }
                try { _ = await s3.HeadAsync(MLPlatform.ControlPlane.Application.Data.Buckets.Datasets, "__health", stop); health.Component("s3", true, "Operational"); }
                catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException) { health.Component("s3", false, "Cannot connect; data and model uploads/downloads are affected"); }
                try
                {
                    using var res = await http.CreateClient().GetAsync(new Uri(new Uri(cfg["Ray:DashboardUrl"] ?? "http://127.0.0.1:8265/"), "api/version"), stop);
                    health.Component("ray", res.IsSuccessStatusCode, res.IsSuccessStatusCode ? "Operational" : "Ray head error; new jobs are queued, running jobs are not affected");
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { health.Component("ray", false, "Ray head connection lost; new jobs are queued, running jobs are not affected"); }
                foreach (var i in health.Snapshot())
                {
                    _last.TryGetValue(i.Name, out var prev);
                    if (prev is not null && prev != i.Status)
                        await m.Send(new EmitSystemEventCommand($"system:{i.Name}", i.Status == "ok" ? "system.recovered" : i.Group == "loop" ? "system.loop_stalled" : "system.component_down",
                            i.Status == "ok" ? "success" : "error", $"{SystemHealth.Label(i.Name)}: {i.Message}"), stop);
                    _last[i.Name] = i.Status;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Health check failed"); }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
