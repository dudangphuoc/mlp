using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Monitoring;

internal static class DriftJob
{
    public static async Task<SnapshotRow> RunAsync(IWriteSession db, IClock clock, IEnumerable<IDriftListener> listeners, Guid deploymentId, int lastN, CancellationToken ct)
    {
        var cfg = await db.GetConfigAsync(deploymentId, ct);
        if (cfg?.Baseline is null) throw new PreconditionException("MONITOR_NO_BASELINE", "No baseline set for the deployment");
        var preds = await db.RecentPredictionsAsync(deploymentId, lastN > 0 ? Math.Min(lastN, 100_000) : cfg.WindowSize, ct);
        if (preds.Count < Math.Min(cfg.MinSamples, lastN > 0 ? lastN : cfg.MinSamples))
            throw new PreconditionException("MONITOR_NOT_ENOUGH_SAMPLES", $"At least {cfg.MinSamples} predictions are required (have {preds.Count})");
        var features = preds.Select(p => JsonNode.Parse(p.Features) as JsonObject ?? []).ToList();
        var predObjs = preds.Select(p => JsonNode.Parse(p.Prediction) as JsonObject ?? []).ToList();
        var (drift, status) = Drift.Compare(JsonNode.Parse(cfg.Baseline)!.AsObject(), features, predObjs.Select(p => p["label"]?.ToString()).ToList(),
            cfg.PsiWarn, cfg.PsiAlert);

        var labeled = preds.Zip(predObjs).Where(x => x.First.GroundTruth is not null)
            .Select(x => (Pred: x.Second, Gt: JsonNode.Parse(x.First.GroundTruth!) as JsonObject ?? [])).ToList();
        var quality = new JsonObject { ["labeled"] = labeled.Count };
        var cls = labeled.Where(x => x.Gt["label"] is not null).ToList();
        if (cls.Count > 0) quality["accuracy"] = Math.Round(cls.Count(x => x.Pred["label"]?.ToString() == x.Gt["label"]!.ToString()) / (double)cls.Count, 4);
        var reg = labeled.Where(x => x.Gt["value"] is not null && x.Pred["value"] is not null).ToList();
        if (reg.Count > 0) quality["mae"] = Math.Round(reg.Average(x => Math.Abs(x.Pred["value"]!.GetValue<double>() - x.Gt["value"]!.GetValue<double>())), 4);

        var previous = (await db.ListSnapshotsAsync(deploymentId, 1, ct)).FirstOrDefault();
        var id = Ids.New();
        var now = clock.UtcNow;
        await db.InsertSnapshotAsync(id, deploymentId, now, preds.Count, status, drift.ToJsonString(), quality.ToJsonString(), ct);
        if (status == "alert" && previous?.Status != "alert" && await db.DeploymentInfoAsync(deploymentId, ct) is { } d)
        {
            var worst = drift.OfType<JsonObject>().Where(r => r["psi"] is not null).OrderByDescending(r => r["psi"]!.GetValue<double>()).First();
            await Notifier.SendAsync(db, clock, d.WorkspaceId, (await Notifier.AdminsAsync(db, d.WorkspaceId, ct)).Append(d.CreatedBy), "deployment.drift",
                $"Deployment {d.Slug} data drift: {worst["feature"]} PSI {worst["psi"]}", "#/deployments/" + d.Id, $"deployment/{d.Id}", "warning", ct);
            foreach (var listener in listeners)
                await listener.OnDriftAlertAsync(d.WorkspaceId, new JsonObject { ["deployment_id"] = d.Id.ToString(), ["deployment"] = d.Slug }, ct);
        }
        return new SnapshotRow(id, now, preds.Count, status, drift.ToJsonString(), quality.ToJsonString());
    }
}
