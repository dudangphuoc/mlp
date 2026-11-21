using System.Collections.Concurrent;
using System.Text;
using MLPlatform.ControlPlane.Application.Ops;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Per-attempt log store (20/10 §2.4): keeps the latest 2,000 lines in memory for realtime viewing; writes
/// everything to S3 storage in ~1 MB chunks
public sealed class LogStore(IServiceScopeFactory scopes, S3Client s3, StreamBroker broker, ILogger<LogStore> log)
{
    private sealed class Buf
    {
        public readonly LinkedList<(long Ts, string Text)> Tail = new();
        public readonly StringBuilder Pending = new();
        public readonly List<string> Trial = [];
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
                if (t != 0 && t < b.LastTs) continue;
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

    /// <summary>The trial lines of an attempt: from memory, or — after an api restart — from the stored log.</summary>
    public async Task<IReadOnlyList<string>> TrialLinesAsync(Guid workspace, Guid job, Guid attempt, CancellationToken ct)
    {
        if (_bufs.TryGetValue(attempt, out var b))
            lock (b) if (b.Trial.Count > 0) return b.Trial.ToList();
        return (await ReadPersistedAsync(workspace, job, attempt, 20_000, ct)).Select(l => l.Text)
            .Where(l => l.Contains("[mlp-trial] ", StringComparison.Ordinal)).ToList();
    }

    /// <summary>Read the last max lines from storage (memory only keeps the newest ones, and nothing after an api
    /// restart).</summary>
    public async Task<IReadOnlyList<(long Ts, string Text)>> ReadPersistedAsync(Guid workspace, Guid job, Guid attempt, int max, CancellationToken ct)
    {
        var prefix = $"{workspace}/jobs/{job}/attempts/{attempt}/logs/";
        var chunks = new List<List<(long, string)>>();
        var count = 0;
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

    /// <summary>Write one attempt's buffered lines now (its container exited: the log must be complete in storage, not
    /// only in memory).</summary>
    public async Task FlushAsync(Guid attempt)
    {
        if (!_bufs.TryGetValue(attempt, out var b)) return;
        string? data = null;
        lock (b) if (b.Pending.Length > 0) { data = b.Pending.ToString(); b.Pending.Clear(); b.Seq++; }
        if (data is not null) await PersistAsync(attempt, b, b.Seq, data);
    }

    private async Task PersistAsync(Guid attempt, Buf b, int seq, string data)
    {
        var key = $"{b.WorkspaceId}/jobs/{b.JobId}/attempts/{attempt}/logs/{TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds():D15}-{seq:D4}.log";
        try
        {
            await s3.PutAsync(MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts, key, Encoding.UTF8.GetBytes(data), "text/plain; charset=utf-8", CancellationToken.None);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or S3Exception or TaskCanceledException)
        {
            lock (b) b.Pending.Insert(0, data);
            log.LogWarning("Saving log {Attempt} to storage failed (kept for the next flush): {M}", attempt, e.Message);
        }
    }
}
