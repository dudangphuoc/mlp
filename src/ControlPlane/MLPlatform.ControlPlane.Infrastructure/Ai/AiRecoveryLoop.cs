using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.Cqrs;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Startup cleanup + retest of failed models every 5 minutes (each retest is a visible run with trigger
/// auto-recovery).</summary>
public sealed class AiRecoveryLoop(AiRuntime ai, IServiceScopeFactory scopes, SystemHealth health, ILogger<AiRecoveryLoop> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AbandonStaleAiRunsCommand(), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "AI stale-run cleanup failed"); }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            health.Beat("ai-recovery", 300);
            try
            {
                ai.Invalidate();
                var cat = await ai.CatalogAsync(ct);
                var conns = cat.Connections.ToDictionary(c => c.Id);
                foreach (var m in cat.Models.Where(m => m.IsActive && m.Health == "failed" && conns.TryGetValue(m.ConnectionId, out var c) && c.IsActive))
                    await ai.TestAsync(m.Id, null, null, "auto-recovery", ct);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "AI recovery pass failed"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
