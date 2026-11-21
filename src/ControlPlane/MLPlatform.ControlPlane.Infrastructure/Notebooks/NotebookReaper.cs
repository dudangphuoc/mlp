using MLPlatform.ControlPlane.Application.Common;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using MLPlatform.AgentProtocol;
using MLPlatform.ControlPlane.Application.Notebooks;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;
using MLPlatform.Cqrs;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>R4: every minute; warn 10 minutes ahead (M16), idle past idle_timeout → stop.</summary>
public sealed class NotebookReaper(IServiceScopeFactory scopes, WorkloadLauncher launcher, SystemHealth health, ILogger<NotebookReaper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("notebook-reaper", 60);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var m = scope.ServiceProvider.GetRequiredService<IMediator>();
                await m.Send(new WarnIdleNotebooksCommand(), stop);
                foreach (var job in await m.Send(new ReconcileStoppingNotebooksCommand(), stop))
                    try { await launcher.StopJobAsync(job, stop); }
                    catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Resending the stop for job {Job} failed", job); }
                foreach (var s in await m.Query(new ListIdleNotebooksQuery(), stop))
                {
                    await m.Send(new MarkNotebookStoppingCommand(s.Id, $"Stopped automatically after {s.IdleTimeoutMin} minutes idle"), stop);
                    if (s.JobId is { } job) await launcher.StopJobAsync(job, stop);
                    log.LogInformation("Notebook {Id} idle for over {Min} minutes → stopping", s.Id, s.IdleTimeoutMin);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Idle notebook cleanup failed");
            }
        }
    }
}
