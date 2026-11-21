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

public sealed record StartPipelineRunCommand(Guid WorkspaceId, Guid PipelineId, string ParamsJson) : ICommand<PipelineRunRow>;

public sealed class StartPipelineRunCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<StartPipelineRunCommand, PipelineRunRow>
{
    public async ValueTask<PipelineRunRow> Handle(StartPipelineRunCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var p = await db.GetPipelineAsync(c.PipelineId, c.WorkspaceId, ct) ?? throw new NotFoundException("PIPELINE_NOT_FOUND", "Pipeline not found");
        var id = await PipelineDef.CreateRunAsync(db, clock, p, PipelineRuns.ParamsOrThrow(c.ParamsJson), "manual", uid, ct);
        return (await db.GetPipelineRunAsync(id, c.WorkspaceId, ct))!;
    }
}
