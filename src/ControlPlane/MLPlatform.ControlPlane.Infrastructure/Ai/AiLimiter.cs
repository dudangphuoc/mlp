using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.Cqrs;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Per-connection concurrency (semaphore) and requests-per-minute window. In-flight counts are shown live in the UI.</summary>
public sealed class AiLimiter(IClock clock)
{
    private sealed class Gate(int max)
    {
        public readonly int Max = max;
        public readonly SemaphoreSlim Slots = new(max, max);
        public readonly Queue<DateTimeOffset> Recent = new();
        public int InFlight;
    }
    private readonly ConcurrentDictionary<Guid, Gate> _g = new();

    public int InFlight(Guid connection) => _g.TryGetValue(connection, out var g) ? Volatile.Read(ref g.InFlight) : 0;

    private Gate For(AiConnectionRow c)
    {
        var g = _g.GetOrAdd(c.Id, _ => new Gate(c.MaxConcurrency));
        if (g.Max == c.MaxConcurrency) return g;
        var fresh = new Gate(c.MaxConcurrency);   // limit changed in the UI — new calls use the new gate
        _g[c.Id] = fresh;
        return fresh;
    }

    /// <summary>Waits for a slot until the deadline. Throws busy / rate-limited (shown as the attempt outcome).</summary>
    public async Task<IDisposable> AcquireAsync(AiConnectionRow c, CancellationToken deadline)
    {
        var g = For(c);
        if (c.RpmLimit > 0)
        {
            lock (g.Recent)
            {
                var cut = clock.UtcNow.AddMinutes(-1);
                while (g.Recent.Count > 0 && g.Recent.Peek() < cut) g.Recent.Dequeue();
                if (g.Recent.Count >= c.RpmLimit)
                    throw new AiCallException("rate-limited", $"Connection {c.Name} reached its limit of {c.RpmLimit} requests per minute");
                g.Recent.Enqueue(clock.UtcNow);
            }
        }
        try { await g.Slots.WaitAsync(deadline); }
        catch (OperationCanceledException)
        {
            throw new AiCallException("busy", $"All {c.MaxConcurrency} slots of connection {c.Name} stayed busy until the deadline");
        }
        Interlocked.Increment(ref g.InFlight);
        return new Release(g);
    }

    private sealed class Release(Gate g) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 1) return;
            Interlocked.Decrement(ref g.InFlight);
            g.Slots.Release();
        }
    }
}
