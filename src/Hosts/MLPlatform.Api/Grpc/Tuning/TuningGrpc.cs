using MLPlatform.ControlPlane.Application.Common;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Tuning;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Grpc;

public sealed class TuningGrpc(IMediator mediator) : TuningService.TuningServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<Sweep> CreateSweep(CreateSweepRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateSweepCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Name, r.Recipe, r.Variant,
            r.Task, r.Algorithm, r.SpaceJson, r.BaseParamsJson, r.Objective, r.Direction, r.MaxTrials, r.Parallel, r.Seed, r.Cpus, r.Gpus,
            string.IsNullOrEmpty(r.DatasetVersionId) ? null : G(r.DatasetVersionId, "dataset_version_id"), r.ImageOverride, r.Network), ctx.CancellationToken));

    public override async Task<ListSweepsResponse> ListSweeps(ListSweepsRequest r, ServerCallContext ctx)
    {
        var res = new ListSweepsResponse();
        res.Sweeps.AddRange((await mediator.Query(new ListSweepsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<SweepDetail> GetSweep(SweepRef r, ServerCallContext ctx)
    {
        var (s, trials) = await mediator.Query(new GetSweepQuery(G(r.WorkspaceId, "workspace_id"), G(r.SweepId, "sweep_id")), ctx.CancellationToken);
        var res = new SweepDetail { Sweep = ToProto(s) };
        foreach (var t in trials)
        {
            var pt = new Trial { Number = t.Number, ParamsJson = t.Params, RunId = t.RunId?.ToString() ?? "", State = t.State, Error = t.Error ?? "",
                CreatedAt = Timestamp.FromDateTimeOffset(t.CreatedAt) };
            if (t.Value is { } v) pt.Value = v;
            res.Trials.Add(pt);
        }
        return res;
    }

    public override async Task<Sweep> StopSweep(SweepRef r, ServerCallContext ctx)
    {
        var (s, _) = await mediator.Send(new StopSweepCommand(G(r.WorkspaceId, "workspace_id"), G(r.SweepId, "sweep_id")), ctx.CancellationToken);
        return ToProto(s);
    }

    private static Sweep ToProto(SweepRow s)
    {
        var p = new Sweep
        {
            Id = s.Id.ToString(), ProjectId = s.ProjectId.ToString(), Name = s.Name, Recipe = s.Recipe, Variant = s.Variant, Algorithm = s.Algorithm,
            SpaceJson = s.Space, Objective = s.Objective, Direction = s.Direction, MaxTrials = s.MaxTrials, Parallel = s.Parallel, State = s.State,
            StateReason = s.StateReason ?? "", CreatedAt = Timestamp.FromDateTimeOffset(s.CreatedAt),
            FinishedAt = s.FinishedAt is { } f ? Timestamp.FromDateTimeOffset(f) : null,
        };
        if (s.BestTrial is { } b) p.BestTrial = b;
        if (s.BestValue is { } v) p.BestValue = v;
        return p;
    }
}
