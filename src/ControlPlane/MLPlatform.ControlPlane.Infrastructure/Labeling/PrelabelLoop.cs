using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Runs batch pre-label jobs one item at a time (low load), with progress and cancellation visible in the
/// project.</summary>
public sealed class PrelabelLoop(IServiceScopeFactory scopes, LabelProposer proposer, SystemHealth health, ILogger<PrelabelLoop> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(ct))
        {
            health.Beat("prelabel", 3);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                foreach (var job in await mediator.Query(new ListOpenPrelabelJobsQuery(), ct))
                    await RunAsync(mediator, job, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Pre-label pass failed"); }
        }
    }

    private async Task RunAsync(IMediator mediator, OpenPrelabelJobRow job, CancellationToken ct)
    {
        int done = job.Done, failed = job.Failed;
        var settings = await mediator.Query(new GetAssistSettingsInternalQuery(job.WorkspaceId, job.ProjectId), ct);
        await mediator.Send(new UpdatePrelabelJobCommand(job.Id, done, failed, "running", null), ct);
        while (done + failed < job.Total)
        {
            if (await mediator.Query(new GetPrelabelJobStateQuery(job.Id, job.WorkspaceId), ct) == "cancelled") return;
            var paths = await mediator.Query(new NextPrelabelPathsQuery(job.ProjectId, job.Id, 1), ct);
            if (paths.Count == 0) break;
            try
            {
                var ctx = await mediator.Query(new GetLabelContextInternalQuery(job.ProjectId, paths[0], job.CreatedBy), ct);
                var set = await proposer.ProposeAsync(mediator, ctx, settings, job.SourceKind, job.SourceId, true, "auto", job.Id, ct);
                if (set.State == "failed") failed++; else done++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                await mediator.Send(new UpdatePrelabelJobCommand(job.Id, done, failed, "failed", e.Message), ct);
                return;
            }
            await mediator.Send(new UpdatePrelabelJobCommand(job.Id, done, failed, "running", null), ct);
        }
        await mediator.Send(new UpdatePrelabelJobCommand(job.Id, done, failed, failed > 0 && done == 0 ? "failed" : "succeeded",
            failed > 0 ? $"{failed} item(s) could not be proposed; open them to see why" : null), ct);
    }
}
