using System.Collections.Concurrent;
using MLPlatform.Data;
using Npgsql;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>StreamBroker (P0-7, 20/02 §5.5): one `LISTEN mlp_ws` connection per api.</summary>
public sealed class StreamBroker(DataSources db, SystemHealth health, ILogger<StreamBroker> log) : BackgroundService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _signals = new();

    public static string Ws(Guid id) => "ws:" + id;
    public static string Attempt(Guid id) => "attempt:" + id;

    /// <summary>Signal a change for a key (wakes every stream waiting on it).</summary>
    public void Pulse(string key)
    {
        if (_signals.TryRemove(key, out var tcs)) tcs.TrySetResult();
    }

    /// <summary>Wait until one of the keys changes, or <paramref name="max"/> elapses.</summary>
    public async Task WaitAsync(IEnumerable<string> keys, TimeSpan max, CancellationToken ct)
    {
        var tasks = keys.Select(k => _signals.GetOrAdd(k, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task).ToList();
        tasks.Add(Task.Delay(max, ct));
        await Task.WhenAny(tasks);
        ct.ThrowIfCancellationRequested();
        await Task.Delay(150, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var conn = await db.Write.OpenConnectionAsync(stop);
                conn.Notification += (_, e) => Pulse(e.Channel == "mlp_ws" ? "ws:" + e.Payload : e.Channel);
                await using (var cmd = new NpgsqlCommand("LISTEN mlp_ws; LISTEN mlp_outbox", conn)) await cmd.ExecuteNonQueryAsync(stop);
                health.Component("stream-broker", true, "LISTEN mlp_ws");
                while (!stop.IsCancellationRequested) await conn.WaitAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception e) when (e is NpgsqlException or System.Net.Sockets.SocketException or IOException)
            {
                health.Component("stream-broker", false, "LISTEN lost; streams re-poll every 5 s");
                log.LogWarning("StreamBroker lost its connection: {Msg}", e.Message);
                foreach (var k in _signals.Keys) Pulse(k);
                await Task.Delay(TimeSpan.FromSeconds(3), stop);
            }
        }
    }
}
