using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Infrastructure;
using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Services;

public sealed class MonitoringGrpc(IMediator mediator) : MonitoringService.MonitoringServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<MonitorStatus> GetMonitor(MonitorRef r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetMonitorQuery(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id")), ctx.CancellationToken));

    public override async Task<MonitorStatus> ConfigureMonitor(ConfigureMonitorRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var dep = G(r.DeploymentId, "deployment_id");
        await mediator.Send(new ConfigureMonitorCommand(ws, dep, G(r.DatasetVersionId, "dataset_version_id"), r.LabelColumn, r.PsiWarn, r.PsiAlert,
            r.WindowSize, r.MinSamples, r.Enabled), ctx.CancellationToken);
        return ToProto(await mediator.Query(new GetMonitorQuery(ws, dep), ctx.CancellationToken));
    }

    public override async Task<DriftSnapshot> ComputeDrift(ComputeDriftRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new ComputeDriftCommand(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id"), r.LastN), ctx.CancellationToken));

    public override async Task<ListSnapshotsResponse> ListSnapshots(ListSnapshotsRequest r, ServerCallContext ctx)
    {
        var res = new ListSnapshotsResponse();
        res.Snapshots.AddRange((await mediator.Query(new ListSnapshotsQuery(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id"), r.Limit),
            ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<QualitySeries> GetQuality(GetQualityRequest r, ServerCallContext ctx)
    {
        var res = new QualitySeries();
        foreach (var q in await mediator.Query(new GetQualityQuery(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id"), r.Days), ctx.CancellationToken))
        {
            var p = new QualityPoint { Day = Timestamp.FromDateTimeOffset(q.Day), Labeled = q.Labeled };
            if (q.Accuracy is { } a) p.Accuracy = a;
            if (q.Mae is { } m) p.Mae = m;
            res.Points.Add(p);
        }
        return res;
    }

    public override async Task<ListPredictionsResponse> ListPredictions(ListPredictionsRequest r, ServerCallContext ctx)
    {
        var res = new ListPredictionsResponse();
        res.Predictions.AddRange((await mediator.Query(new ListPredictionsQuery(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id"), r.Limit),
            ctx.CancellationToken)).Select(p => new PredictionRecord
            {
                Id = p.Id.ToString(), At = Timestamp.FromDateTimeOffset(p.At), LatencyMs = p.LatencyMs, FeaturesJson = p.Features,
                PredictionJson = p.Prediction, GroundTruthJson = p.GroundTruth ?? "",
            }));
        return res;
    }

    private static MonitorStatus ToProto(MonitorView v)
    {
        var res = new MonitorStatus { Configured = v.Config?.Baseline is not null };
        if (v.Config is { } c)
        {
            res.Config = new MonitorConfig
            {
                DatasetVersionId = c.DatasetVersionId?.ToString() ?? "", LabelColumn = c.LabelColumn, BaselineRows = c.BaselineRows, PsiWarn = c.PsiWarn,
                PsiAlert = c.PsiAlert, WindowSize = c.WindowSize, MinSamples = c.MinSamples, Enabled = c.Enabled, UpdatedAt = Timestamp.FromDateTimeOffset(c.UpdatedAt),
            };
            if (c.Baseline is not null && JsonNode.Parse(c.Baseline)?["features"] is JsonObject f) res.Config.Features.AddRange(f.Select(kv => kv.Key));
        }
        if (v.Latest is { } s) res.Latest = ToProto(s);
        return res;
    }

    private static DriftSnapshot ToProto(SnapshotRow s)
    {
        var d = new DriftSnapshot { Id = s.Id.ToString(), At = Timestamp.FromDateTimeOffset(s.At), Samples = s.Samples, Status = s.Status };
        foreach (var r in JsonNode.Parse(s.Drift)!.AsArray().OfType<JsonObject>())
        {
            var f = new FeatureDrift { Feature = r["feature"]!.ToString(), Kind = r["kind"]!.ToString(), Samples = r["samples"]?.GetValue<int>() ?? 0, Status = r["status"]!.ToString() };
            if (r["psi"] is JsonValue psi) f.Psi = psi.GetValue<double>();
            if (r["ks"] is JsonValue ks) f.Ks = ks.GetValue<double>();
            if (r["baseline_mean"] is JsonValue bm) f.BaselineMean = bm.GetValue<double>();
            if (r["current_mean"] is JsonValue cm) f.CurrentMean = cm.GetValue<double>();
            d.Features.Add(f);
        }
        var q = JsonNode.Parse(s.Quality)!.AsObject();
        d.Labeled = q["labeled"]?.GetValue<int>() ?? 0;
        if (q["accuracy"] is JsonValue a) d.Accuracy = a.GetValue<double>();
        if (q["mae"] is JsonValue m) d.Mae = m.GetValue<double>();
        return d;
    }
}
