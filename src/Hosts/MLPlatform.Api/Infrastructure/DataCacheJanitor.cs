using MLPlatform.Contracts.V1;
using MLPlatform.ControlPlane.Application.Ops;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Infrastructure;

/// <summary>
/// Removes host dataset cache volumes automatically (DataCacheJanitorRules): idle for two weeks, or idle and the machine is short of disk.
/// Every 30 minutes; only machines that are connected; each removal is recorded on the machine's event stream.
/// </summary>
public sealed class DataCacheJanitor(IServiceScopeFactory scopes, AgentRegistry agents, ILogger<DataCacheJanitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stop);
        while (!stop.IsCancellationRequested)
        {
            try { await SweepAsync(stop); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Dataset cache clean-up failed"); }
            await Task.Delay(TimeSpan.FromMinutes(30), stop);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        foreach (var (cache, reason) in await m.Send(new PlanDataCacheCleanupCommand(), ct))
        {
            if (!agents.TryGet(cache.HostId, out var s)) continue;   // offline: try again next sweep
            var rm = new ContainerRemove { Label = "mlp.none=" + Guid.NewGuid().ToString("N") };   // no container: volume only
            rm.Volumes.Add(MLPlatform.ControlPlane.Application.Data.DataCacheRules.Volume(cache.DatasetId));
            var res = await s.SendCommandAsync(new AgentCommand { ContainerRemove = rm }, TimeSpan.FromMinutes(2), ct);
            if (!res.Ok) { log.LogWarning("Removing dataset cache {Dataset} on {Host} failed: {Msg}", cache.DatasetId, cache.HostId, res.Message); continue; }
            await m.Send(new RecordDataCacheCleanupCommand(cache, reason), ct);
            log.LogInformation("Dataset cache {Dataset} on {Host} removed: {Reason}", cache.DatasetId, cache.HostId, reason);
        }
    }
}
