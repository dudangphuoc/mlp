using System.Collections.Concurrent;
using System.Text;
using MLPlatform.ControlPlane.Application.Ops;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>System health (20/10 §2.6): components (PostgreSQL, S3 storage, Ray head, agent channel) checked every 15
/// s; each background loop records its own heartbeat.</summary>
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

    /// <summary>Called by a background loop on each pass.</summary>
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
