using System.Text.Json;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Ray;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>
/// Replica probing (M12 R2/R5): every 5 s call ModelServer.Health; success → healthy, failure → degraded (the gateway drops it from routing).
/// </summary>
public sealed class DeploymentProber(IServiceScopeFactory scopes, ModelServerClients clients, SystemHealth health, WorkloadLauncher launcher,
    ILogger<DeploymentProber> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("deployment-prober", 5);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var m = scope.ServiceProvider.GetRequiredService<IMediator>();
                // Playground test replicas that nobody called for idle_stop_min minutes: free the GPU.
                foreach (var d in await m.Query(new MLPlatform.ControlPlane.Application.Serving.ListIdleTestDeploymentsQuery(), stop))
                {
                    await m.Send(new MLPlatform.ControlPlane.Application.Serving.StopIdleDeploymentCommand(d.Id, d.IdleStopMin ?? 0), stop);
                    if (d.JobId is { } job) await launcher.StopJobAsync(job, stop);
                    log.LogInformation("Test replica {Slug} stopped after {Min} idle minutes", d.Slug, d.IdleStopMin);
                }
                foreach (var d in await m.Query(new MLPlatform.ControlPlane.Application.Serving.ListProbeTargetsQuery(), stop))
                {
                    var ok = await clients.HealthAsync(d.Endpoint!, stop);
                    var next = ok ? "healthy" : d.State == "healthy" ? "degraded" : d.State;
                    if (next != d.State)
                    {
                        await m.Send(new MLPlatform.ControlPlane.Application.Serving.SetDeploymentHealthCommand(d.Id, next, ok ? null : "Replica is not responding"), stop);
                        log.LogInformation("Deployment {Slug}: {From} → {To}", d.Slug, d.State, next);
                    }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Deployment probe failed");
            }
        }
    }
}
