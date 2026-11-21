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

/// <summary>One circuit breaker per model, kept for the process lifetime: 3 failures in a row open it for 30 s, then
/// one probe call.</summary>
public sealed class AiCircuits(IClock clock)
{
    private sealed class State { public int Failures; public DateTimeOffset OpenUntil; public bool Probing; }
    private readonly ConcurrentDictionary<Guid, State> _s = new();
    private static readonly TimeSpan OpenFor = TimeSpan.FromSeconds(30);

    public string Describe(Guid model)
    {
        if (!_s.TryGetValue(model, out var st)) return "closed";
        lock (st) return st.Failures < 3 ? "closed" : clock.UtcNow < st.OpenUntil ? "open" : "half-open";
    }

    /// <summary>True when a call may go out; false = open (the caller records "circuit-open" and moves on).</summary>
    public bool TryEnter(Guid model)
    {
        var st = _s.GetOrAdd(model, _ => new State());
        lock (st)
        {
            if (st.Failures < 3) return true;
            if (clock.UtcNow < st.OpenUntil || st.Probing) return false;
            st.Probing = true;
            return true;
        }
    }

    public void Report(Guid model, bool ok)
    {
        var st = _s.GetOrAdd(model, _ => new State());
        lock (st)
        {
            st.Probing = false;
            if (ok) { st.Failures = 0; return; }
            st.Failures++;
            if (st.Failures >= 3) st.OpenUntil = clock.UtcNow + OpenFor;
        }
    }

    public void Reset(Guid model) => _s.TryRemove(model, out _);
}
