using System.Collections.Concurrent;
using System.Text;
using MLPlatform.ControlPlane.Application.Ops;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

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
