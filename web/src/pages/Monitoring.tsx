import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ago } from "../lib/format";
import { ErrorBox, Field, PageHeader, StateBadge } from "../components/ui";
import { MetricChart } from "../components/MetricChart";
import { translate as t } from "../i18n";
import { useCrumb } from "../lib/crumbs";

const driftTone: Record<string, string> = { ok: "text-emerald-700", warn: "text-amber-700", alert: "text-rose-700", missing: "text-slate-400" };

/** M13 — deployment Monitoring tab: baseline + thresholds, per-feature drift, daily actual quality, latest prediction log. */
export function DeploymentMonitorPage({ ws, id, onBack }: { ws: string; id: string; onBack: () => void }) {
  const qc = useQueryClient();
  const deps = useQuery({ queryKey: ["deployments", ws], queryFn: () => api.serving.listDeployments({ workspaceId: ws }) });
  const dep = deps.data?.deployments.find((d) => d.id === id);
  useCrumb(id, dep?.slug);
  const status = useQuery({ queryKey: ["monitor", ws, id], queryFn: () => api.monitoring.getMonitor({ workspaceId: ws, deploymentId: id }), refetchInterval: 10000 });
  const snaps = useQuery({ queryKey: ["snapshots", ws, id], queryFn: () => api.monitoring.listSnapshots({ workspaceId: ws, deploymentId: id, limit: 50 }), refetchInterval: 10000 });
  const quality = useQuery({ queryKey: ["quality", ws, id], queryFn: () => api.monitoring.getQuality({ workspaceId: ws, deploymentId: id, days: 30 }) });
  const preds = useQuery({ queryKey: ["predictions", ws, id], queryFn: () => api.monitoring.listPredictions({ workspaceId: ws, deploymentId: id, limit: 20 }), refetchInterval: 10000 });
  const datasets = useQuery({ queryKey: ["datasets", ws, ""], queryFn: () => api.data.listDatasets({ workspaceId: ws, task: "" }) });
  const [datasetId, setDatasetId] = useState("");
  const versions = useQuery({ queryKey: ["versions", ws, datasetId], enabled: !!datasetId, queryFn: () => api.data.listVersions({ workspaceId: ws, datasetId }) });
  const [f, setF] = useState({ versionId: "", labelColumn: "label", psiWarn: 0.1, psiAlert: 0.25 });
  const refresh = () => ["monitor", "snapshots", "quality", "predictions"].forEach((k) => qc.invalidateQueries({ queryKey: [k, ws, id] }));
  const configure = useMutation({
    mutationFn: () => api.monitoring.configureMonitor({ workspaceId: ws, deploymentId: id, datasetVersionId: f.versionId, labelColumn: f.labelColumn,
      psiWarn: f.psiWarn, psiAlert: f.psiAlert, windowSize: 500, minSamples: 20, enabled: true }),
    onSuccess: refresh,
  });
  const compute = useMutation({ mutationFn: () => api.monitoring.computeDrift({ workspaceId: ws, deploymentId: id, lastN: 0 }), onSuccess: refresh });
  const latest = status.data?.latest;
  const cfg = status.data?.config;
  const history = [...(snaps.data?.snapshots ?? [])].reverse();
  const featureNames = [...new Set(history.flatMap((s) => s.features.filter((x) => x.psi !== undefined).map((x) => x.feature)))];
  const psiSeries = featureNames.map((n) => ({ name: n, points: history.map((s, i) => [i, s.features.find((x) => x.feature === n)?.psi ?? 0] as [number, number]) }));
  const qPoints = (quality.data?.points ?? []).filter((p) => p.accuracy !== undefined).map((p, i) => [i, p.accuracy!] as [number, number]);

  return (
    <>
      <button className="mb-3 text-sm text-brand-500" onClick={onBack}>← Deployments</button>
      <PageHeader title={t("Monitoring — {s}", { s: dep?.slug ?? "" })} subtitle={dep && `${dep.modelName} v${dep.modelVersion} · ${dep.task}`}
        actions={status.data?.configured && <button className="btn-ghost" disabled={compute.isPending} onClick={() => compute.mutate()}>{t("Compute drift now")}</button>} />
      <ErrorBox error={configure.error ? errorText(configure.error) : compute.error ? errorText(compute.error) : null} />

      <div className="mb-4 grid gap-4 md:grid-cols-3">
        <div className="card space-y-2">
          <div className="text-sm font-medium">{t("Baseline & thresholds")}</div>
          {cfg?.datasetVersionId ? (
            <div className="text-xs text-slate-600">{t("{n} rows", { n: String(cfg.baselineRows) })} · feature: {cfg.features.join(", ")} · {t("label:")} {cfg.labelColumn} · {t("PSI warn {w} / alert {a}", { w: cfg.psiWarn, a: cfg.psiAlert })}</div>
          ) : <div className="text-xs text-slate-500">{t("No baseline yet — pick the dataset version used for training.")}</div>}
          <select className="input" value={datasetId} onChange={(e) => { setDatasetId(e.target.value); setF({ ...f, versionId: "" }); }}>
            <option value="">— dataset —</option>
            {(datasets.data?.datasets ?? []).map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
          <select className="input" value={f.versionId} disabled={!datasetId} onChange={(e) => setF({ ...f, versionId: e.target.value })}>
            <option value="">— version —</option>
            {(versions.data?.versions ?? []).map((v) => <option key={v.id} value={v.id}>v{v.number}</option>)}
          </select>
          <div className="grid grid-cols-3 gap-2">
            <Field label={t("Label column")}><input className="input" value={f.labelColumn} onChange={(e) => setF({ ...f, labelColumn: e.target.value })} /></Field>
            <Field label={t("PSI warning")}><input className="input" type="number" step="0.05" value={f.psiWarn} onChange={(e) => setF({ ...f, psiWarn: Number(e.target.value) })} /></Field>
            <Field label={t("PSI alert")}><input className="input" type="number" step="0.05" value={f.psiAlert} onChange={(e) => setF({ ...f, psiAlert: Number(e.target.value) })} /></Field>
          </div>
          <button className="btn-primary w-full" disabled={!f.versionId || configure.isPending} onClick={() => configure.mutate()}>{t("Set baseline")}</button>
        </div>
        <div className="card md:col-span-2">
          <div className="mb-2 flex items-center justify-between text-sm font-medium">
            <span>{t("Latest drift")}</span>
            {latest && <span className="flex items-center gap-2 text-xs text-slate-500"><StateBadge state={latest.status} />{t("{n} predictions", { n: latest.samples })} · {latest.at ? ago(latest.at) : ""}</span>}
          </div>
          {latest ? (
            <table className="w-full text-sm">
              <thead><tr className="text-left text-xs text-slate-500"><th>Feature</th><th>{t("Kind")}</th><th>PSI</th><th>KS</th><th>{t("Mean baseline → current")}</th><th>{t("Level")}</th></tr></thead>
              <tbody>{latest.features.map((x) => (
                <tr key={x.feature} className="border-t border-slate-100">
                  <td className="py-1 font-medium">{x.feature}</td><td className="text-xs">{x.kind}</td>
                  <td>{x.psi?.toFixed(3) ?? "—"}</td><td>{x.ks?.toFixed(3) ?? "—"}</td>
                  <td className="text-xs">{x.baselineMean !== undefined ? `${x.baselineMean.toFixed(2)} → ${x.currentMean?.toFixed(2)}` : ""}</td>
                  <td className={`text-xs font-semibold ${driftTone[x.status] ?? ""}`}>{x.status}</td>
                </tr>))}</tbody>
            </table>
          ) : <div className="text-sm text-slate-500">{t("No drift snapshot yet. It is computed once there are enough new predictions, or click \"Compute drift now\".")}</div>}
          {latest && latest.labeled > 0 && (
            <div className="mt-2 text-xs text-slate-600">{t("Actual quality on {n} labeled predictions:", { n: latest.labeled })} {latest.accuracy !== undefined ? `accuracy ${(latest.accuracy * 100).toFixed(1)}%` : ""}{latest.mae !== undefined ? ` MAE ${latest.mae.toFixed(3)}` : ""}</div>)}
        </div>
      </div>

      <div className="mb-4 grid gap-4 md:grid-cols-2">
        {psiSeries.length > 0 && <MetricChart title={t("PSI per computation")} series={psiSeries} height={200} />}
        {qPoints.length > 0 && <MetricChart title={t("Real accuracy by day")} series={[{ name: "accuracy", points: qPoints }]} height={200} />}
      </div>

      <div className="card overflow-x-auto">
        <div className="mb-2 text-sm font-medium">{t("Recent predictions")}</div>
        <table className="w-full min-w-[640px] text-sm">
          <thead><tr className="text-left text-xs text-slate-500"><th>{t("At")}</th><th>{t("Input")}</th><th>{t("Predictions")}</th><th>{t("Ground truth")}</th><th>ms</th></tr></thead>
          <tbody>{(preds.data?.predictions ?? []).map((p) => (
            <tr key={p.id} className="border-t border-slate-100 font-mono text-xs">
              <td className="py-1 font-sans">{p.at ? ago(p.at) : ""}</td><td>{p.featuresJson}</td><td>{p.predictionJson}</td>
              <td>{p.groundTruthJson || "—"}</td><td>{p.latencyMs.toFixed(1)}</td>
            </tr>))}</tbody>
        </table>
        {preds.data?.predictions.length === 0 && <div className="text-sm text-slate-500">{t("No predictions through the gateway yet.")}</div>}
      </div>
    </>
  );
}
