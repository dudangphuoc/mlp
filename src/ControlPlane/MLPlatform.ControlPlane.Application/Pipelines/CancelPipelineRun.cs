using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Pipelines;

/// <summary>Cancels a run; returns running jobs so the host stops the containers.</summary>
public sealed record CancelPipelineRunCommand(Guid WorkspaceId, Guid RunId) : ICommand<(PipelineRunRow Run, IReadOnlyList<Guid> Jobs)>;

public sealed class CancelPipelineRunCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator, IAfterCommit afterCommit, IWorkloadControl workloads)
    : ICommandHandler<CancelPipelineRunCommand, (PipelineRunRow Run, IReadOnlyList<Guid> Jobs)>
{
    public async ValueTask<(PipelineRunRow Run, IReadOnlyList<Guid> Jobs)> Handle(CancelPipelineRunCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var r = await db.GetPipelineRunAsync(c.RunId, c.WorkspaceId, ct) ?? throw new NotFoundException("PIPELINE_RUN_NOT_FOUND", "Pipeline run not found");
        var jobs = new List<Guid>();
        foreach (var s in (await db.ListStepsAsync(r.Id, ct)).Where(s => s.State == "running" && s.RefId is not null))
        {
            if (s.Kind == "train") { if ((await mediator.Query(new GetRunQuery(c.WorkspaceId, s.RefId!.Value), ct)).JobId is { } j) jobs.Add(j); }
            else if (s.Kind == "job") jobs.Add(s.RefId!.Value);
            await db.UpdateStepAsync(r.Id, s.StepId, "failed", s.Attempt, s.RefId, s.Output, "Cancelled", clock.UtcNow, ct);
        }
        await db.FinishPipelineRunAsync(r.Id, "cancelled", "Cancelled by user", clock.UtcNow, ct);
        afterCommit.StopAfterCommit(workloads, jobs);
        return ((await db.GetPipelineRunAsync(r.Id, c.WorkspaceId, ct))!, jobs);
    }
}
