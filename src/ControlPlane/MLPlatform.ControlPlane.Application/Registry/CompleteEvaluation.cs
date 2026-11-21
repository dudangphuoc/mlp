using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Computes the metrics from the stored samples, merges test/* into the model version, notifies the
/// requester.</summary>
[Access(AccessKind.Internal)]
public sealed record CompleteEvaluationCommand(Guid EvaluationId) : ICommand<bool>, IInternalCommand;

public sealed class CompleteEvaluationCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<CompleteEvaluationCommand, bool>
{
    public async ValueTask<bool> Handle(CompleteEvaluationCommand c, CancellationToken ct)
    {
        var e = await db.GetEvaluationAsync(c.EvaluationId, ct) ?? throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
        var mv = (await db.GetModelVersionAsync(e.ModelVersionId, e.WorkspaceId, ct))!;
        var rows = (await db.AllEvalSamplesAsync(e.Id, ct)).Where(r => r.Error is null).ToList();
        if (rows.Count == 0) throw new PreconditionException("EVAL_NO_SAMPLES", "No sample could be evaluated");
        JsonObject metrics;
        JsonArray perClass;
        JsonObject? confusion = null;
        if (EvalMath.DetectTasks.Contains(mv.Task))
        {
            var images = rows.Select(r => ((IReadOnlyList<EvalBox>)Boxes(r.Gt), (IReadOnlyList<EvalBox>)Boxes(r.Pred))).ToList();
            var s = EvalMath.Detection(images, e.ConfThreshold);
            var sweep = EvalMath.ThresholdSweep(images);
            var best = sweep.MaxBy(x => x.F1);
            metrics = new JsonObject
            {
                ["test/mAP50-95"] = R(s.Map5095), ["test/mAP50"] = R(s.Map50), ["test/precision"] = R(s.Precision), ["test/recall"] = R(s.Recall),
                ["test/f1"] = R(s.F1), ["test/tp"] = s.Tp, ["test/fp"] = s.Fp, ["test/fn"] = s.Fn,
                ["test/best_conf"] = best.Conf, ["test/best_f1"] = R(best.F1), ["test/best_precision"] = R(best.Precision), ["test/best_recall"] = R(best.Recall),
            };
            confusion = new JsonObject
            {
                ["sweep"] = new JsonArray(sweep.Select(x => (JsonNode)new JsonObject { ["conf"] = x.Conf, ["precision"] = R(x.Precision), ["recall"] = R(x.Recall), ["f1"] = R(x.F1) }).ToArray()),
            };
            perClass = new JsonArray(s.PerClass.Select(p => (JsonNode)new JsonObject
            {
                ["class"] = p.Cls, ["gt"] = p.Gt, ["pred"] = p.Pred, ["ap50"] = R(p.Ap50), ["ap50_95"] = R(p.Ap5095),
                ["precision"] = R(p.Precision), ["recall"] = R(p.Recall),
            }).ToArray());
        }
        else
        {
            var s = EvalMath.Classification(rows.Select(r => (Label(r.Gt), Label(r.Pred))).ToList());
            metrics = new JsonObject { ["test/accuracy"] = R(s.Accuracy), ["test/f1_macro"] = R(s.F1Macro) };
            perClass = new JsonArray(s.PerClass.Select(p => (JsonNode)new JsonObject
            {
                ["class"] = p.Cls, ["gt"] = p.Support, ["precision"] = R(p.Precision), ["recall"] = R(p.Recall), ["f1"] = R(p.F1),
            }).ToArray());
            confusion = new JsonObject
            {
                ["labels"] = new JsonArray(s.Labels.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()),
                ["matrix"] = new JsonArray(s.Confusion.Select(row => (JsonNode)new JsonArray(row.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray())).ToArray()),
            };
        }
        metrics["test/samples"] = rows.Count;
        metrics["test/latency_ms"] = Math.Round(rows.Average(r => r.LatencyMs), 1);
        await db.CompleteEvaluationAsync(e.Id, metrics.ToJsonString(), perClass.ToJsonString(), confusion?.ToJsonString(), clock.UtcNow, ct);
        await db.MergeVersionMetricsAsync(mv.Id, metrics.ToJsonString(), ct);
        var headline = metrics["test/mAP50-95"] is { } m ? $"mAP50-95 {m}" : $"accuracy {metrics["test/accuracy"]}";
        await Notifier.SendAsync(db, clock, e.WorkspaceId, [e.CreatedBy], "model.evaluated", $"Evaluation of {mv.ModelName} v{mv.Version} finished: {headline}",
            $"#/models?eval={e.Id}", $"evaluation/{e.Id}", "success", ct);
        return true;
    }

    private static double R(double v) => Math.Round(v, 4);

    private static List<EvalBox> Boxes(string json) =>
        (JsonNode.Parse(json) as JsonArray ?? []).OfType<JsonObject>().Select(o =>
        {
            var b = o["b"]!.AsArray();
            return new EvalBox(o["c"]!.ToString(), b[0]!.GetValue<double>(), b[1]!.GetValue<double>(), b[2]!.GetValue<double>(), b[3]!.GetValue<double>(),
                o["s"]?.GetValue<double>() ?? 1);
        }).ToList();

    private static string Label(string json) => (JsonNode.Parse(json) as JsonObject)?["c"]?.ToString() ?? "";
}
