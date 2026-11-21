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

[RequiresPermission("run.read")]
public sealed record GetPipelineRunQuery(Guid WorkspaceId, Guid RunId) : IQuery<(PipelineRunRow Run, IReadOnlyList<StepRow> Steps, string Definition)>;

public sealed class GetPipelineRunQueryHandler(IReadSession db, WorkspaceAuthorizer auth)
    : IQueryHandler<GetPipelineRunQuery, (PipelineRunRow Run, IReadOnlyList<StepRow> Steps, string Definition)>
{
    public async ValueTask<(PipelineRunRow Run, IReadOnlyList<StepRow> Steps, string Definition)> Handle(GetPipelineRunQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var r = await db.GetPipelineRunAsync(q.RunId, q.WorkspaceId, ct) ?? throw new NotFoundException("PIPELINE_RUN_NOT_FOUND", "Pipeline run not found");
        var p = await db.GetPipelineAsync(r.PipelineId, q.WorkspaceId, ct);
        return (r, await db.ListStepsAsync(r.Id, ct), p?.Definition ?? "{}");
    }
}
