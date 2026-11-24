using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Every 30 seconds marks code runs whose process stopped sending heartbeats as failed.</summary>
public sealed class CodeRunJanitor(IServiceScopeFactory scopes, ILogger<CodeRunJanitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stop);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ExpireCodeRunsCommand(), stop);
                if (expired > 0) log.LogInformation("Marked {Count} silent code runs as failed", expired);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Code run expiry failed"); }
        }
    }
}
