using System.Text.Json;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Ray;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>D-36 (20/10 §4.4): a `suspect` attempt past its wait for the host to return → `node_lost` → the scheduler
/// retries (same or another host) from the checkpoint.</summary>
public sealed class RecoveryLoop(IServiceScopeFactory scopes, WorkloadLauncher launcher, SystemHealth health, ILogger<RecoveryLoop> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                health.Beat("recovery", 5);
                await using var scope = scopes.CreateAsyncScope();
                var med = scope.ServiceProvider.GetRequiredService<IMediator>();
                foreach (var s in await med.Query(new MLPlatform.ControlPlane.Application.Ops.ListExpiredSuspectsQuery(), stop))
                {
                    log.LogWarning("Attempt {Attempt} timed out waiting for host → node_lost", s.Id);
                    await launcher.FailAsync(s.Id, "node_lost", "The host did not come back within the wait period", stop);
                }
                foreach (var id in await med.Send(new MLPlatform.ControlPlane.Application.Scheduling.ExpireReservationsCommand(TimeSpan.FromMinutes(3)), stop))
                {
                    log.LogWarning("Attempt {Attempt} held resources for over 10 minutes → cancelling holder, requeueing", id);
                    await launcher.AbandonReservationAsync(id, stop);
                }
                foreach (var id in await med.Send(new MLPlatform.ControlPlane.Application.Ops.DetectAnomaliesCommand(), stop))
                {
                    log.LogWarning("Attempt {Attempt} lost signal for too long → stopping and retrying", id);
                    await launcher.StopAttemptAsync(id, stop);
                    await launcher.FailAsync(id, "signal_lost", "The training process has been silent too long; stopping and retrying from the checkpoint", stop);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Recovery loop failed"); }
        }
    }
}
