using MLPlatform.ControlPlane.Application.Common;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Pipelines;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Pipeline = MLPlatform.Contracts.V1.Pipeline;

namespace MLPlatform.Api.Grpc;

public sealed class PipelineGrpc(IMediator mediator) : PipelineService.PipelineServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    private static Timestamp? Ts(DateTimeOffset? t) => t is { } v ? Timestamp.FromDateTimeOffset(v) : null;

    public override async Task<Pipeline> CreatePipeline(CreatePipelineRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreatePipelineCommand(G(r.WorkspaceId, "workspace_id"), r.Name, r.DefinitionJson, r.TriggerJson), ctx.CancellationToken));

    public override async Task<ListPipelinesResponse> ListPipelines(ListPipelinesRequest r, ServerCallContext ctx)
    {
        var res = new ListPipelinesResponse();
        res.Pipelines.AddRange((await mediator.Query(new ListPipelinesQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<PipelineRun> StartRun(StartPipelineRunRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new StartPipelineRunCommand(G(r.WorkspaceId, "workspace_id"), G(r.PipelineId, "pipeline_id"), r.ParamsJson), ctx.CancellationToken));

    public override async Task<ListPipelineRunsResponse> ListRuns(ListPipelineRunsRequest r, ServerCallContext ctx)
    {
        var res = new ListPipelineRunsResponse();
        res.Runs.AddRange((await mediator.Query(new ListPipelineRunsQuery(G(r.WorkspaceId, "workspace_id"),
            string.IsNullOrEmpty(r.PipelineId) ? null : G(r.PipelineId, "pipeline_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<PipelineRunDetail> GetRun(PipelineRunRef r, ServerCallContext ctx)
    {
        var (run, steps, definition) = await mediator.Query(new GetPipelineRunQuery(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id")), ctx.CancellationToken);
        var defs = PipelineDef.Parse(definition).Steps;
        var res = new PipelineRunDetail { Run = ToProto(run) };
        foreach (var d in defs)
        {
            var s = steps.FirstOrDefault(x => x.StepId == d.Id);
            var ps = new PipelineStep { Id = d.Id, Kind = d.Kind, State = s?.State ?? "pending", Attempt = s?.Attempt ?? 0, RefId = s?.RefId?.ToString() ?? "",
                OutputJson = s?.Output ?? "{}", Error = s?.Error ?? "", StartedAt = Ts(s?.StartedAt), FinishedAt = Ts(s?.FinishedAt) };
            ps.Needs.AddRange(d.Needs);
            res.Steps.Add(ps);
        }
        return res;
    }

    public override async Task<PipelineRun> RetryRun(RetryPipelineRunRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new RetryPipelineRunCommand(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id"), r.ParamsJson), ctx.CancellationToken));

    public override async Task<PipelineRun> CancelRun(PipelineRunRef r, ServerCallContext ctx)
    {
        var (run, _) = await mediator.Send(new CancelPipelineRunCommand(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id")), ctx.CancellationToken);
        return ToProto(run);
    }

    private static Pipeline ToProto(PipelineRow p) => new()
    {
        Id = p.Id.ToString(), Name = p.Name, DefinitionJson = p.Definition, TriggerJson = p.Trigger, Enabled = p.Enabled,
        LastTriggeredAt = Ts(p.LastTriggeredAt), CreatedAt = Ts(p.CreatedAt),
    };

    private static PipelineRun ToProto(PipelineRunRow r) => new()
    {
        Id = r.Id.ToString(), PipelineId = r.PipelineId.ToString(), PipelineName = r.PipelineName, Number = r.Number, State = r.State, ParamsJson = r.Params,
        Trigger = r.Trigger, Error = r.Error ?? "", CreatedAt = Ts(r.CreatedAt), FinishedAt = Ts(r.FinishedAt),
    };
}
