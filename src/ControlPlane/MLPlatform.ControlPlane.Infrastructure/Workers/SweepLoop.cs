using MLPlatform.ControlPlane.Application.Common;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Tuning;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>
/// Sweep orchestrator (M10, D-21): every 3 s advances each running sweep in a scope acting as the sweep's creator; trials are ordinary M09 runs,
/// audited as that user's StartTraining. Trials do not run inside Ray Tune.
/// </summary>
public sealed class SweepLoop(IServiceScopeFactory scopes, SystemHealth health, ILogger<SweepLoop> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("sweep", 3);
            try
            {
                IReadOnlyList<SweepRow> sweeps;
                await using (var s0 = scopes.CreateAsyncScope())
                    sweeps = await s0.ServiceProvider.GetRequiredService<IMediator>().Query(new ListRunningSweepsQuery(), stop);
                foreach (var s in sweeps)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<CurrentUser>().ActAs(s.CreatedBy);
                    try
                    {
                        var n = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AdvanceSweepCommand(s.WorkspaceId, s.Id), stop);
                        if (n > 0) log.LogInformation("Sweep {Sweep}: sinh {N} trial", s.Id, n);
                    }
                    catch (AppException e) { log.LogWarning("Sweep {Sweep} failed: {Reason} {Msg}", s.Id, e.Reason, e.Message); }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Sweep orchestration loop failed");
            }
        }
    }
}
