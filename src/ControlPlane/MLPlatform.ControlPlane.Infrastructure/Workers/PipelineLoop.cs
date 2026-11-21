using MLPlatform.ControlPlane.Application.Common;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Pipelines;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Pipeline = MLPlatform.Contracts.V1.Pipeline;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Pipeline engine (M14, D-21): every 3 s fire due periodic triggers, then advance each run in a scope acting as the user who started it.</summary>
public sealed class PipelineLoop(IServiceScopeFactory scopes, SystemHealth health, ILogger<PipelineLoop> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("pipeline", 3);
            try
            {
                IReadOnlyList<PipelineRunRow> runs;
                await using (var s0 = scopes.CreateAsyncScope())
                {
                    var m = s0.ServiceProvider.GetRequiredService<IMediator>();
                    await m.Send(new FireIntervalTriggersCommand(), stop);
                    runs = await m.Query(new ListActivePipelineRunsQuery(), stop);
                }
                foreach (var r in runs)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<CurrentUser>().ActAs(r.CreatedBy);
                    try { await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AdvancePipelineRunCommand(r.WorkspaceId, r.Id), stop); }
                    catch (AppException e) { log.LogWarning("Pipeline run {Run} failed: {Reason} {Msg}", r.Id, e.Reason, e.Message); }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Pipeline loop failed"); }
        }
    }
}
