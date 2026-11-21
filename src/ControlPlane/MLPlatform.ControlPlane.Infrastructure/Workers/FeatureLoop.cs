using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Features;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>M07: every minute, materialize feature views whose schedule is due.</summary>
public sealed class FeatureLoop(IServiceScopeFactory scopes, SystemHealth health, ILogger<FeatureLoop> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        health.Beat("feature", 60);
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("feature", 60);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new MaterializeDueCommand(), stop);
                if (n > 0) log.LogInformation("Materialized {N} scheduled feature view(s)", n);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Materialize loop failed"); }
        }
    }
}
