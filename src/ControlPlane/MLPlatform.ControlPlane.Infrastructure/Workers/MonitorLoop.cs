using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>M13: every `Monitoring:IntervalSeconds` (default 300) compute drift for deployments with enough new predictions; alerts → M16.</summary>
public sealed class MonitorLoop(IServiceScopeFactory scopes, IConfiguration cfg, SystemHealth health, ILogger<MonitorLoop> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var every = int.TryParse(cfg["Monitoring:IntervalSeconds"], out var s) && s >= 10 ? s : 300;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(every));
        health.Beat("monitor", every);
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("monitor", every);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new MonitorTickCommand(), stop);
                if (n > 0) log.LogInformation("Monitoring: computed drift for {N} deployment(s)", n);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Monitoring loop failed"); }
        }
    }
}
