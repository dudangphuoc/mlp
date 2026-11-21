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

/// <summary>Rerun from the failed step (M14 §1): keeps outputs of succeeded steps, resets failed + skipped steps;
/// parameters may be changed.</summary>
[RequiresPermission("job.submit")]
public sealed record RetryPipelineRunCommand(Guid WorkspaceId, Guid RunId, string ParamsJson) : ICommand<PipelineRunRow>;

public sealed class RetryPipelineRunCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<RetryPipelineRunCommand, PipelineRunRow>
{
    public async ValueTask<PipelineRunRow> Handle(RetryPipelineRunCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var r = await db.GetPipelineRunAsync(c.RunId, c.WorkspaceId, ct) ?? throw new NotFoundException("PIPELINE_RUN_NOT_FOUND", "Pipeline run not found");
        if (r.State is not ("failed" or "cancelled")) throw new PreconditionException("PIPELINE_RUN_NOT_FAILED", "Only failed or cancelled runs can be rerun");
        var p = JsonNode.Parse(r.Params) as JsonObject ?? [];
        foreach (var (k, v) in PipelineRuns.ParamsOrThrow(c.ParamsJson) ?? []) p[k] = v?.DeepClone();
        foreach (var s in await db.ListStepsAsync(r.Id, ct))
            if (s.State is "failed" or "skipped" or "running" or "pending") await db.ResetStepAsync(r.Id, s.StepId, ct);
        await db.ReopenPipelineRunAsync(r.Id, p.ToJsonString(), ct);
        return (await db.GetPipelineRunAsync(r.Id, c.WorkspaceId, ct))!;
    }
}
