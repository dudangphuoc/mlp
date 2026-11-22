using MLPlatform.ControlPlane.Application.Ops.Gpu;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Every 5 minutes rolls GPU samples into 1-minute rows and prunes old samples.</summary>
public sealed class GpuHistoryJanitor(IServiceScopeFactory scopes, ILogger<GpuHistoryJanitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stop);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RollupGpuSamplesCommand(), stop);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "GPU history rollup failed"); }
            await Task.Delay(TimeSpan.FromMinutes(5), stop);
        }
    }
}
