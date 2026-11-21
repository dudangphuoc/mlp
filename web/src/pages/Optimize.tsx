import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, Gauge, Play, Plus, Rocket, Save, Trash2 } from "lucide-react";
import { api, errorText } from "../lib/api";
import { bytes } from "../lib/format";
import { useCrumb } from "../lib/crumbs";
import { ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, PageHeader, RelTime, StateBadge } from "../components/ui";
import { DataTable } from "../components/DataTable";
import { translate as t } from "../i18n";
import type { Optimization } from "../gen/mlplatform/v1/registry_pb";

/* Model optimization for production (v1): candidates built from the ONNX export, checked on the same images, benchmarked on the target machine. */

type Lat = { p50_ms: number; p95_ms: number; p99_ms: number; mean_ms: number; throughput_ips: number; peak_rss_mb: number; warmup: number; iters: number; batch: number };
type Place = { providers: string[]; nodes_per_ep: Record<string, number>; memcpy_nodes: number; load_rss_mb: number };
type Fid = { match_rate?: number; boxes_source?: number; matched?: number; missed?: number; extra?: number; score_drift_mean?: number; score_drift_max?: number; top1_agreement?: number; images: number; conf?: number };
type Cand = { size_bytes: number; sha256: string; build_seconds: number; settings: Record<string, unknown>; placement: Place; latency: Lat; fidelity: Fid; ops: Record<string, number> };
type Report = {
  state?: string; error?: string; spec?: { seed?: string; threads?: number; calib_method?: string; candidates?: string[] };
  host?: { cpu: string; cpu_count: number; threads: number; device: string; gpu: string };
  versions?: Record<string, string>; source?: { size_bytes: number; sha256: string; opset: number; ir_version: number; ops: Record<string, number>; placement: Place; latency: Lat; inputs: { name: string; dtype: string; shape: (number | string)[] }[]; outputs: { name: string; dtype: string; shape: (number | string)[] }[] };
  candidates?: Record<string, Cand>; errors?: Record<string, string>;
  worst?: { file: string; image: number; path: string; candidate: string; source_boxes: number; candidate_boxes: number }[];
};
const parse = <T,>(s: string, d: T): T => { try { return (JSON.parse(s || "null") as T) ?? d; } catch { return d; } };
const LABEL: Record<string, string> = { source: "FP32", fp32: "FP32 opt", fp16: "FP16", int8: "INT8" };
const COLOR: Record<string, string> = { source: "#888780", fp32: "#7F77DD", fp16: "#1D9E75", int8: "#D85A30" };
const pct = (v?: number) => (v === undefined ? "—" : `${(v * 100).toFixed(1)}%`);
const epShare = (p?: Place) => {
  if (!p) return "—";
  const tot = Object.values(p.nodes_per_ep).reduce((a, b) => a + b, 0);
  if (tot === 0) return p.providers[0]?.replace("ExecutionProvider", "") ?? "—";
  return Object.entries(p.nodes_per_ep).sort((a, b) => b[1] - a[1]).map(([ep, n]) => `${ep.replace("ExecutionProvider", "")} ${Math.round((n / tot) * 100)}%`).join(" · ");
};

/** List of optimization runs + the sheet that starts one. */
export function OptimizationsPage({ ws, go }: { ws: string; go: (p: string) => void }) {
  const list = useQuery({
    queryKey: ["optimizations", ws], queryFn: () => api.registry.listOptimizations({ workspaceId: ws }),
    refetchInterval: (q) => (q.state.data?.optimizations.some((o) => o.state === "running") ? 4000 : false),
  });
  const [creating, setCreating] = useState(() => new URLSearchParams(window.location.search).get("version") ?? (null as string | null));
  const rows = list.data?.optimizations ?? [];
  return (
    <>
      <PageHeader title={t("Optimization")} actions={<button className="btn-primary" onClick={() => setCreating("")}><Plus className="h-4 w-4" />{t("Optimize")}</button>} />
      <ErrorBox error={list.error ? errorText(list.error) : null} />
      {rows.length === 0 && !list.isLoading ? <EmptyState title={t("No optimizations yet")} action={<button className="btn-primary" onClick={() => setCreating("")}>{t("Optimize")}</button>} /> : (
        <DataTable rows={rows} loading={list.isLoading} rowKey={(o) => o.id} onRowClick={(o) => go(`optimize/${o.id}`)}
          columns={[
            { key: "model", header: "Model", value: (o) => o.modelName, cell: (o) => <span className="font-medium">{o.modelName} <span className="text-fg-subtle">v{o.sourceVersion}</span></span> },
            { key: "device", header: t("Device"), value: (o) => o.device, cell: (o) => <span className="font-mono text-xs uppercase">{o.device}</span> },
            { key: "cands", header: t("Candidates"), cell: (o) => <span className="font-mono text-xs">{o.candidates.map((c) => LABEL[c] ?? c).join(" · ")}</span> },
            { key: "best", header: "p50", cell: (o) => <BestLine o={o} /> },
            { key: "state", header: t("Status"), value: (o) => o.state, cell: (o) => <StateBadge state={o.state} reason={o.error || undefined} /> },
            { key: "created", header: t("Created"), value: (o) => Number(o.createdAt?.seconds ?? 0), cell: (o) => <span className="text-xs"><RelTime at={o.createdAt} /></span> },
          ]} />)}
      {creating !== null && <NewOptimizationSheet ws={ws} versionId={creating} onClose={() => setCreating(null)} onStarted={(id) => { setCreating(null); go(`optimize/${id}`); }} />}
    </>
  );
}

function BestLine({ o }: { o: Optimization }) {
  const r = parse<Report>(o.reportJson, {});
  if (!r.source || !r.candidates) return <span className="text-fg-subtle">—</span>;
  const best = Object.entries(r.candidates).sort((a, b) => a[1].latency.p50_ms - b[1].latency.p50_ms)[0];
  if (!best) return <span className="text-fg-subtle">—</span>;
  return <span className="tabular-nums text-xs">{r.source.latency.p50_ms} → <b>{best[1].latency.p50_ms} ms</b> <span className="text-fg-subtle">{LABEL[best[0]]}</span></span>;
}

/** Start a run: source version (with its ONNX export), device, candidates, machine, check and calibration images. */
function NewOptimizationSheet({ ws, versionId, onClose, onStarted }: { ws: string; versionId: string; onClose: () => void; onStarted: (id: string) => void }) {
  const qc = useQueryClient();
  const models = useQuery({ queryKey: ["models", ws], queryFn: () => api.registry.listModels({ workspaceId: ws }) });
  const allVersions = useQuery({
    queryKey: ["all-versions", ws], enabled: !!models.data,
    queryFn: async () => (await Promise.all((models.data?.models ?? []).map((m) => api.registry.listModelVersions({ workspaceId: ws, modelId: m.id })))).flatMap((r) => r.versions),
  });
  const versions = (allVersions.data ?? []).filter((v) => ["detect", "classify"].includes(v.task) && v.source !== "optimize");
  const [src, setSrc] = useState(versionId);
  const source = versions.find((v) => v.id === src);
  const exports = useQuery({
    queryKey: ["exports", ws, src], enabled: !!src, queryFn: () => api.registry.listExports({ workspaceId: ws, modelVersionId: src }),
    refetchInterval: (q) => (q.state.data?.exports.some((e) => e.state === "running") ? 3000 : false),
  });
  const onnx = (exports.data?.exports ?? []).filter((e) => e.format === "onnx");
  const onnxReady = onnx.find((e) => e.state === "ready");
  const onnxRunning = onnx.find((e) => e.state === "running");
  const canExport = (exports.data?.formats ?? []).includes("onnx");
  const exportOnnx = useMutation({ mutationFn: () => api.registry.startExport({ workspaceId: ws, modelVersionId: src, format: "onnx" }), onSuccess: () => void qc.invalidateQueries({ queryKey: ["exports", ws, src] }) });

  const map = useQuery({ queryKey: ["cluster-map", ws], queryFn: () => api.ops.getClusterMap({ workspaceId: ws }) });
  const hosts = (map.data?.hosts ?? []).filter((h) => h.state === "online" || h.state === "busy");
  const [hostId, setHostId] = useState("");
  const host = hosts.find((h) => h.id === hostId) ?? hosts[0];
  useEffect(() => { if (!hostId && hosts[0]) setHostId(hosts[0].id); }, [hosts.length]); // eslint-disable-line react-hooks/exhaustive-deps
  const hasGpu = (host?.gpus.length ?? 0) > 0;
  const [device, setDevice] = useState<"cuda" | "cpu">("cuda");
  useEffect(() => { if (host && !hasGpu) setDevice("cpu"); }, [host?.id]); // eslint-disable-line react-hooks/exhaustive-deps
  const allowed = device === "cuda" ? ["fp32", "fp16"] : ["fp32", "int8"];
  const [picked, setPicked] = useState<string[]>(["fp32", "fp16", "int8"]);
  const cands = allowed.filter((c) => picked.includes(c));

  const datasets = useQuery({ queryKey: ["datasets", ws, ""], queryFn: () => api.data.listDatasets({ workspaceId: ws, task: "" }) });
  const dsList = (datasets.data?.datasets ?? []).filter((d) => !source || d.task === source.task);
  const [checkDs, setCheckDs] = useState("");
  const checkVersions = useQuery({ queryKey: ["versions", ws, checkDs], enabled: !!checkDs, queryFn: () => api.data.listVersions({ workspaceId: ws, datasetId: checkDs }) });
  const [checkVer, setCheckVer] = useState("");
  useEffect(() => { const v = checkVersions.data?.versions[0]; if (v) setCheckVer(v.id); }, [checkVersions.data]);
  const [nCheck, setNCheck] = useState("200");
  const [nCalib, setNCalib] = useState("300");
  const [method, setMethod] = useState("minmax");
  const [conf, setConf] = useState("0.5");
  const start = useMutation({
    mutationFn: () => api.registry.startOptimization({
      workspaceId: ws, sourceVersionId: src, device, candidates: cands, hostId: host?.id ?? "", checkDatasetVersionId: checkVer, checkImages: Number(nCheck),
      calibDatasetVersionId: cands.includes("int8") ? checkVer : "", calibImages: Number(nCalib), conf: Number(conf), calibMethod: method,
    }),
    onSuccess: (o) => { void qc.invalidateQueries({ queryKey: ["optimizations", ws] }); onStarted(o.id); },
  });
  const ready = !!src && !!onnxReady && cands.length > 0 && !!checkVer && Number(nCheck) >= 10;
  return (
    <Dialog title={t("Optimize")} onClose={onClose} wide>
      <div className="space-y-5 text-sm">
        <Field label={t("Source")}>
          {!allVersions.data ? <div className="skeleton h-9" /> : <select className="input" value={src} onChange={(e) => setSrc(e.target.value)}>
            <option value="">—</option>
            {versions.map((v) => <option key={v.id} value={v.id}>{v.modelName} v{v.version} · {v.task}</option>)}
          </select>}
        </Field>
        {src && (
          <div className="flex flex-wrap items-center gap-2 rounded-lg border border-line px-3 py-2">
            <span className="font-mono text-xs">ONNX</span>
            {exports.isPending ? <span className="skeleton h-4 w-24" />
              : onnxReady ? <><StateBadge state="ready" /><span className="font-mono text-xs text-fg-muted">{onnxReady.fileName} · {bytes(onnxReady.sizeBytes)}</span></>
              : onnxRunning ? <StateBadge state="running" />
              : canExport ? <button className="btn-ghost h-7 px-2 text-xs" disabled={exportOnnx.isPending} onClick={() => exportOnnx.mutate()}><Play className="h-3.5 w-3.5" />{t("Convert")}</button>
              : <span className="text-xs text-danger">{t("This model's recipe has no conversion")}</span>}
          </div>)}
        <div className="grid gap-3 md:grid-cols-2">
          <Field label={t("Machine")}>
            <select className="input" value={host?.id ?? ""} onChange={(e) => setHostId(e.target.value)}>
              {hosts.map((h) => <option key={h.id} value={h.id}>{h.name} · {h.cpus} CPU{h.gpus.length ? ` · ${h.gpus.map((g) => g.name).join(", ")}` : ""}</option>)}
            </select>
          </Field>
          <Field label={t("Device")}>
            <div className="inline-flex rounded-lg border border-line p-0.5 text-xs" role="tablist">
              {(["cuda", "cpu"] as const).map((d) => (
                <button key={d} type="button" role="tab" aria-selected={device === d} disabled={d === "cuda" && !hasGpu} onClick={() => setDevice(d)}
                  className={`rounded-md px-3 py-1.5 ${device === d ? "bg-primary/10 font-medium text-primary" : "text-fg-muted hover:text-fg"} disabled:opacity-40`}>{d === "cuda" ? "GPU · ONNX Runtime CUDA" : "CPU · ONNX Runtime"}</button>))}
            </div>
          </Field>
        </div>
        <Field label={t("Candidates")}>
          <div className="flex flex-wrap gap-2">
            {allowed.map((c) => (
              <label key={c} className={`flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 ${picked.includes(c) ? "border-primary bg-primary/10" : "border-line"}`}>
                <input type="checkbox" checked={picked.includes(c)} onChange={(e) => setPicked(e.target.checked ? [...picked, c] : picked.filter((x) => x !== c))} />
                <span className="h-2.5 w-2.5 rounded-full" style={{ background: COLOR[c] }} /><span className="font-mono text-xs">{LABEL[c]}</span>
              </label>))}
          </div>
        </Field>
        <div className="grid gap-3 md:grid-cols-3">
          <Field label={t("Dataset")}>
            <select className="input" value={checkDs} onChange={(e) => { setCheckDs(e.target.value); setCheckVer(""); }}>
              <option value="">—</option>{dsList.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </Field>
          <Field label={t("Version")}>
            <select className="input" value={checkVer} onChange={(e) => setCheckVer(e.target.value)}>
              <option value="">—</option>{(checkVersions.data?.versions ?? []).map((v) => <option key={v.id} value={v.id}>v{v.number}</option>)}
            </select>
          </Field>
          <Field label={t("Check images (val)")}><input className="input" inputMode="numeric" value={nCheck} onChange={(e) => setNCheck(e.target.value)} /></Field>
        </div>
        <div className="grid gap-3 md:grid-cols-3">
          <Field label={t("Confidence")}><input className="input" inputMode="decimal" value={conf} onChange={(e) => setConf(e.target.value)} /></Field>
          {cands.includes("int8") && <>
            <Field label={t("Calibration images (train)")}><input className="input" inputMode="numeric" value={nCalib} onChange={(e) => setNCalib(e.target.value)} /></Field>
            <Field label={t("Calibration")}>
              <select className="input" value={method} onChange={(e) => setMethod(e.target.value)}><option value="minmax">MinMax</option><option value="percentile">Percentile</option></select>
            </Field>
          </>}
        </div>
        <ErrorBox error={[start.error, exportOnnx.error, allVersions.error].filter(Boolean).map((e) => errorText(e))[0] ?? null} />
        <button className="btn-primary" disabled={!ready || start.isPending} onClick={() => start.mutate()}><Gauge className="h-4 w-4" />{t("Optimize")}</button>
      </div>
    </Dialog>
  );
}

/** One run: steps, candidate table, latency × fidelity chart, worst disagreements, ops before/after, package and actions. */
/** Build/job failures carry the whole build log; keep the code and the tail (the full text is on the job log page). */
function shortError(e: string) {
  if (e.length <= 500) return e;
  const code = /^[A-Z_]+:/.exec(e)?.[0] ?? "";
  const tail = e.lastIndexOf("ERROR");
  return `${code} … ${e.slice(tail > 0 ? tail : e.length - 400).slice(0, 400)}`;
}

export function OptimizationPage({ ws, id, go, onBack }: { ws: string; id: string; go: (p: string) => void; onBack: () => void }) {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ["optimization", ws, id], queryFn: () => api.registry.getOptimization({ workspaceId: ws, id }), refetchInterval: (x) => (x.state.data?.state === "running" ? 4000 : false) });
  const o = q.data;
  useCrumb(id, o ? `${o.modelName} v${o.sourceVersion}` : undefined);
  const r = useMemo(() => parse<Report>(o?.reportJson ?? "", {}), [o?.reportJson]);
  const promoted = useMemo(() => parse<Record<string, string>>(o?.promotedJson ?? "", {}), [o?.promotedJson]);
  const [sel, setSel] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [evaluating, setEvaluating] = useState(false);
  const file = useMutation({ mutationFn: (path: string) => api.registry.getOptimizationFile({ workspaceId: ws, id, path }), onSuccess: (x) => window.location.assign(x.url) });
  const promote = useMutation({
    mutationFn: (cand: string) => api.registry.promoteOptimization({ workspaceId: ws, id, candidate: cand }),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["optimization", ws, id] }); void qc.invalidateQueries({ queryKey: ["models", ws] }); },
  });
  const del = useMutation({ mutationFn: () => api.registry.deleteOptimization({ workspaceId: ws, id }), onSuccess: () => { void qc.invalidateQueries({ queryKey: ["optimizations", ws] }); onBack(); } });
  const worstUrls = useQuery({
    queryKey: ["opt-worst", ws, id, (r.worst ?? []).length], enabled: (r.worst ?? []).length > 0,
    queryFn: async () => Promise.all((r.worst ?? []).map(async (w) => (await api.registry.getOptimizationFile({ workspaceId: ws, id, path: w.file })).url)),
  });
  if (!o) return q.error ? <ErrorBox error={errorText(q.error)} /> : <div className="skeleton h-64" />;
  const cands = Object.entries(r.candidates ?? {});
  const rows: { key: string; c?: Cand }[] = [{ key: "source" }, ...o.candidates.map((k) => ({ key: k, c: r.candidates?.[k] }))];
  const lat = (k: string, c?: Cand) => (k === "source" ? r.source?.latency : c?.latency);
  const err = [file.error, promote.error, del.error].filter(Boolean).map((e) => errorText(e))[0] ?? null;
  const selected = sel ?? cands[0]?.[0] ?? null;
  const steps: [string, string][] = [
    ["ONNX", "done"], [t("Build"), o.state === "running" ? "active" : cands.length ? "done" : "failed"],
    [t("Check"), o.state === "running" ? "pending" : cands.length ? "done" : "failed"],
    [t("Saved"), Object.keys(promoted).length ? "done" : "pending"],
  ];
  return (
    <>
      <button className="mb-3 text-sm text-primary" onClick={onBack}>← {t("Optimization")}</button>
      <PageHeader title={`${o.modelName} v${o.sourceVersion}`} subtitle={<span className="font-mono text-xs uppercase">{o.device} · {o.task}</span>}
        actions={<>
          {o.jobId && <button className="btn-ghost" onClick={() => go(`jobs/${o.jobId}`)}>{t("Log")}</button>}
          {o.state !== "running" && <button className="btn-ghost" onClick={() => go(`optimize?version=${o.sourceVersionId}`)}><Play className="h-4 w-4" />{t("Run again")}</button>}
          <button className="btn-ghost text-danger" onClick={() => setConfirmDelete(true)}><Trash2 className="h-4 w-4" />{t("Delete")}</button>
        </>} />
      <div className="mb-4 flex flex-wrap gap-2 text-xs">
        {steps.map(([l, s]) => <span key={l} className={`rounded-md px-2.5 py-1 ${s === "done" ? "bg-emerald-500/15 text-emerald-500" : s === "active" ? "bg-primary/15 text-primary" : s === "failed" ? "bg-danger/15 text-danger" : "bg-surface-2 text-fg-muted"}`}>{l}</span>)}
        <StateBadge state={o.state} />
      </div>
      <ErrorBox error={err ?? (o.state === "failed" ? shortError(t(o.error)) : null)} />
      {Object.entries(r.errors ?? {}).map(([k, v]) => <div key={k} className="mb-2 rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 font-mono text-xs text-danger">{LABEL[k]}: {v}</div>)}
      {o.state === "running" && !r.source ? <div className="skeleton h-48" /> : r.source && (
        <>
          <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
            <div className="card overflow-x-auto p-0">
              <table className="w-full whitespace-nowrap text-sm [&_td]:px-3 [&_td]:py-2.5 [&_th]:px-3 [&_th]:py-2.5">
                <thead><tr className="text-left text-xs text-fg-muted">
                  <th className="p-3">{t("Candidate")}</th><th>{t("Size")}</th><th>p50</th><th>p95</th><th>p99</th><th>{t("Throughput")}</th><th>RAM</th>
                  <th>{o.task === "classify" ? "Top-1" : t("Box match")}</th><th>{t("Missed / extra")}</th><th>Δ score</th><th>EP</th><th />
                </tr></thead>
                <tbody>{rows.map(({ key, c }) => {
                  const l = lat(key, c), f = c?.fidelity, place = key === "source" ? r.source?.placement : c?.placement;
                  const fallback = o.device === "cuda" && place && ((place.memcpy_nodes ?? 0) > 0 || Object.keys(place.nodes_per_ep).some((ep) => ep.startsWith("CPU")));
                  const size = key === "source" ? r.source!.size_bytes : c?.size_bytes;
                  return (
                    <tr key={key} className={`cursor-pointer border-t border-line ${selected === key ? "bg-primary/5" : "hover:bg-surface-2"}`} onClick={() => key !== "source" && c && setSel(key)}>
                      <td className="p-3"><span className="flex items-center gap-2"><span className="h-2.5 w-2.5 rounded-full" style={{ background: COLOR[key] }} /><span className="font-medium">{LABEL[key] ?? key}</span></span></td>
                      <td className="tabular-nums">{size ? bytes(size) : "—"}{key !== "source" && size && <span className="ml-1 text-xs text-fg-subtle">{Math.round((size / r.source!.size_bytes) * 100)}%</span>}</td>
                      <td className="tabular-nums font-medium">{l ? `${l.p50_ms} ms` : "—"}</td><td className="tabular-nums">{l?.p95_ms ?? "—"}</td><td className="tabular-nums">{l?.p99_ms ?? "—"}</td>
                      <td className="tabular-nums">{l ? `${l.throughput_ips}/s` : "—"}</td><td className="tabular-nums">{l ? `${Math.round(l.peak_rss_mb)} MB` : "—"}</td>
                      <td className={`tabular-nums ${f && (f.match_rate ?? f.top1_agreement ?? 1) < 0.95 ? "text-warning" : ""}`}>{key === "source" ? "—" : pct(f?.match_rate ?? f?.top1_agreement)}</td>
                      <td className="tabular-nums">{f?.missed !== undefined ? `${f.missed} / ${f.extra}` : "—"}</td>
                      <td className="tabular-nums">{f?.score_drift_mean !== undefined ? f.score_drift_mean.toFixed(3) : "—"}</td>
                      <td className={`text-xs ${fallback ? "text-warning" : "text-fg-muted"}`}>{epShare(place)}{place?.memcpy_nodes ? ` · ${place.memcpy_nodes} memcpy` : ""}</td>
                      <td className="pr-3 text-right" onClick={(e) => e.stopPropagation()}>
                        {key !== "source" && c && (
                          <span className="inline-flex gap-1 whitespace-nowrap">
                            <button className="btn-ghost h-7 px-2 text-xs" aria-label={t("Download")} onClick={() => file.mutate(`${key}/model.onnx`)}><Download className="h-3.5 w-3.5" /></button>
                            {promoted[key]
                              ? <button className="btn-ghost h-7 px-2 text-xs" onClick={() => go("deployments?new=1")}><Rocket className="h-3.5 w-3.5" />Deploy</button>
                              : <button className="btn-primary h-7 px-2 text-xs" disabled={promote.isPending} onClick={() => promote.mutate(key)}><Save className="h-3.5 w-3.5" />{t("Save as version")}</button>}
                          </span>)}
                      </td>
                    </tr>);
                })}</tbody>
              </table>
              <div className="flex flex-wrap gap-x-4 gap-y-1 border-t border-line px-3 py-2 text-[11px] text-fg-subtle">
                <span>{r.host?.cpu} · {r.host?.threads} {t("threads")}{r.host?.gpu ? ` · GPU ${r.host.gpu.slice(0, 12)}` : ""}</span>
                <span>warm-up {r.source.latency.warmup} · {r.source.latency.iters} {t("runs")} · batch {r.source.latency.batch}</span>
                <span>{cands[0]?.[1].fidelity.images ?? 0} {t("images")} · conf {cands[0]?.[1].fidelity.conf ?? o.conf} · seed {r.spec?.seed?.slice(0, 8)}</span>
                <span>ORT {r.versions?.onnxruntime} · onnx {r.versions?.onnx} · opset {r.source.opset}</span>
              </div>
            </div>
            <div className="card"><Scatter r={r} /></div>
          </div>

          {(r.worst ?? []).length > 0 && (
            <div className="mt-4">
              <div className="mb-2 text-sm font-medium">{t("Biggest disagreements")}</div>
              <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
                {(r.worst ?? []).map((w, i) => (
                  <figure key={w.file} className="overflow-hidden rounded-lg border border-line">
                    {worstUrls.data?.[i] ? <img src={worstUrls.data[i]} alt={w.path} className="aspect-square w-full object-cover" /> : <div className="skeleton aspect-square" />}
                    <figcaption className="flex items-center justify-between gap-1 px-2 py-1 text-[11px]">
                      <span className="truncate font-mono text-fg-muted" title={w.path}>{w.path.split("/").pop()}</span>
                      <span className="shrink-0 tabular-nums"><span style={{ color: "#22c55e" }}>{w.source_boxes}</span> / <span style={{ color: "#f97316" }}>{w.candidate_boxes}</span> <span className="text-fg-subtle">{LABEL[w.candidate]}</span></span>
                    </figcaption>
                  </figure>))}
              </div>
            </div>)}

          <div className="mt-4 grid gap-4 xl:grid-cols-2">
            {selected && r.candidates?.[selected] && <OpsDiff before={r.source.ops} after={r.candidates[selected].ops} label={LABEL[selected]} settings={r.candidates[selected].settings} />}
            <div className="card space-y-3 text-sm">
              <div className="flex items-center justify-between"><span className="font-medium">{t("Accuracy on the test split")}</span>
                <button className="btn-ghost h-8" disabled={Object.keys(promoted).length === 0} onClick={() => setEvaluating(true)}><Gauge className="h-4 w-4" />{t("Evaluate")}</button></div>
              <AccuracyTable ws={ws} sourceId={o.sourceVersionId} promoted={promoted} />
              <div className="border-t border-line pt-3">
                <div className="mb-1 text-xs text-fg-muted">{t("Package")}</div>
                <div className="flex flex-wrap gap-2">
                  {["config/onnx.json", "signature.json", "report.json", "pipeline.py", "spec.json"].map((p) => (
                    <button key={p} className="btn-ghost h-7 px-2 font-mono text-xs" onClick={() => file.mutate(p)}><Download className="h-3.5 w-3.5" />{p}</button>))}
                </div>
                <div className="mt-2 font-mono text-[11px] text-fg-subtle">sha256 {r.source.sha256.slice(0, 16)}… · {r.source.inputs.map((i) => `${i.name}:${i.dtype}[${i.shape.join(",")}]`).join(" ")}</div>
              </div>
            </div>
          </div>
        </>)}
      {evaluating && <EvaluateBothSheet ws={ws} o={o} promoted={promoted} onClose={() => setEvaluating(false)} />}
      {confirmDelete && <ConfirmDialog title={t("Delete")} label="Delete" confirmText={o.modelName} busy={del.isPending} consequence={t("Saved versions stay.")} onConfirm={() => del.mutate()} onClose={() => setConfirmDelete(false)} />}
    </>
  );
}

/** Latency p50 (x) vs fidelity (y), bubble area = file size; the source is the anchor. */
function Scatter({ r }: { r: Report }) {
  const pts = [{ k: "source", x: r.source!.latency.p50_ms, y: 1, s: r.source!.size_bytes },
    ...Object.entries(r.candidates ?? {}).map(([k, c]) => ({ k, x: c.latency.p50_ms, y: c.fidelity.match_rate ?? c.fidelity.top1_agreement ?? 1, s: c.size_bytes }))];
  const maxX = Math.max(...pts.map((p) => p.x)) * 1.15, minY = Math.min(0.9, ...pts.map((p) => p.y)) - 0.01, maxS = Math.max(...pts.map((p) => p.s));
  const W = 320, H = 210, L = 40, B = 28;
  const X = (v: number) => L + (v / maxX) * (W - L - 10), Y = (v: number) => 10 + ((1.005 - v) / (1.005 - minY)) * (H - B - 10);
  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="mx-auto max-h-72 w-full max-w-xl" role="img" aria-label="p50 / fidelity">
      <line x1={L} y1={H - B} x2={W - 6} y2={H - B} stroke="currentColor" strokeOpacity={0.25} />
      <line x1={L} y1={8} x2={L} y2={H - B} stroke="currentColor" strokeOpacity={0.25} />
      {[0, 0.5, 1].map((f) => <text key={f} x={X(maxX * f / 1.15)} y={H - 10} fontSize={10} textAnchor="middle" className="fill-fg-subtle">{Math.round(maxX * f / 1.15)} ms</text>)}
      {[1, (1 + minY) / 2, minY].map((v) => <text key={v} x={L - 4} y={Y(v) + 3} fontSize={10} textAnchor="end" className="fill-fg-subtle">{(v * 100).toFixed(0)}%</text>)}
      {pts.map((p) => (
        <g key={p.k}>
          <circle cx={X(p.x)} cy={Y(p.y)} r={6 + 16 * Math.sqrt(p.s / maxS)} fill={COLOR[p.k]} fillOpacity={0.35} stroke={COLOR[p.k]} />
          <text x={X(p.x)} y={Y(p.y) - 8 - 16 * Math.sqrt(p.s / maxS)} fontSize={10} textAnchor="middle" className="fill-fg-muted">{LABEL[p.k]}</text>
        </g>))}
    </svg>
  );
}

/** Op types before → after (fusions, Q/DQ inserted, casts) and the candidate's build settings. */
function OpsDiff({ before, after, label, settings }: { before: Record<string, number>; after: Record<string, number>; label: string; settings: Record<string, unknown> }) {
  const keys = [...new Set([...Object.keys(before), ...Object.keys(after)])].map((k) => ({ k, a: before[k] ?? 0, b: after[k] ?? 0 }))
    .filter((x) => x.a !== x.b || x.a > 10).sort((x, y) => Math.abs(y.b - y.a) - Math.abs(x.b - x.a) || y.b - x.b).slice(0, 14);
  const max = Math.max(1, ...keys.flatMap((x) => [x.a, x.b]));
  return (
    <div className="card text-sm">
      <div className="mb-2 font-medium">Ops · FP32 → {label}</div>
      <div className="space-y-1">{keys.map((x) => (
        <div key={x.k} className="grid grid-cols-[8rem_1fr_5rem] items-center gap-2 text-xs">
          <span className="truncate font-mono text-fg-muted">{x.k}</span>
          <span className="relative h-3"><span className="absolute inset-y-0 left-0 rounded bg-fg-subtle/30" style={{ width: `${(x.a / max) * 100}%` }} /><span className="absolute inset-y-[3px] left-0 rounded bg-primary" style={{ width: `${(x.b / max) * 100}%` }} /></span>
          <span className="text-right tabular-nums">{x.a} → <b>{x.b}</b></span>
        </div>))}</div>
      <div className="mt-3 flex flex-wrap gap-1 border-t border-line pt-2">{Object.entries(settings).filter(([, v]) => typeof v !== "object").map(([k, v]) => <span key={k} className="rounded bg-surface-2 px-1.5 py-0.5 font-mono text-[11px] text-fg-muted">{k}={String(v)}</span>)}</div>
    </div>
  );
}

/** Latest finished evaluation per version (source + saved candidates), only rows that share the same samples. */
function AccuracyTable({ ws, sourceId, promoted }: { ws: string; sourceId: string; promoted: Record<string, string> }) {
  const ids = [["source", sourceId], ...Object.entries(promoted)] as [string, string][];
  const evals = useQuery({
    queryKey: ["opt-evals", ws, ids.map((x) => x[1]).join(",")], refetchInterval: 8000,
    queryFn: async () => Promise.all(ids.map(async ([k, v]) => [k, (await api.registry.listEvaluations({ workspaceId: ws, modelVersionId: v })).evaluations] as const)),
  });
  const rows = (evals.data ?? []).map(([k, list]) => {
    const e = list[0];
    const m = parse<Record<string, number>>(e?.metricsJson ?? "", {});
    return { k, e, m };
  });
  if (rows.every((r) => !r.e)) return <div className="text-xs text-fg-subtle">—</div>;
  const key = (e?: { datasetVersionId: string; pathFilter: string; maxSamples: number }) => (e ? `${e.datasetVersionId}|${e.pathFilter}|${e.maxSamples}` : "");
  const ref = key(rows.find((r) => r.k === "source")?.e ?? rows.find((r) => r.e)?.e);
  return (
    <table className="w-full text-xs">
      <thead><tr className="text-left text-fg-muted"><th className="py-1">{t("Candidate")}</th><th>mAP50-95</th><th>mAP50</th><th>Acc</th><th>{t("Images")}</th><th>{t("Status")}</th></tr></thead>
      <tbody>{rows.map(({ k, e, m }) => (
        <tr key={k} className={`border-t border-line ${e && key(e) !== ref ? "text-warning" : ""}`}>
          <td className="py-1.5"><span className="flex items-center gap-1.5"><span className="h-2 w-2 rounded-full" style={{ background: COLOR[k] }} />{k === "source" ? "FP32 (PyTorch)" : LABEL[k]}</span></td>
          <td className="tabular-nums">{m["test/mAP50-95"]?.toFixed(4) ?? "—"}</td><td className="tabular-nums">{m["test/mAP50"]?.toFixed(4) ?? "—"}</td>
          <td className="tabular-nums">{m["test/accuracy"]?.toFixed(4) ?? "—"}</td>
          <td className="tabular-nums">{e ? `${e.done}/${e.total || e.maxSamples}` : "—"}</td>
          <td>{e ? <StateBadge state={e.state} /> : "—"}</td>
        </tr>))}</tbody>
    </table>
  );
}

/** Starts one evaluation per version (source + every saved candidate) with the same dataset version, filter, sample count and threshold. */
function EvaluateBothSheet({ ws, o, promoted, onClose }: { ws: string; o: Optimization; promoted: Record<string, string>; onClose: () => void }) {
  const qc = useQueryClient();
  const [filter, setFilter] = useState("test/");
  const [max, setMax] = useState("300");
  const [conf, setConf] = useState("0.25");
  const run = useMutation({
    mutationFn: async () => {
      for (const v of [o.sourceVersionId, ...Object.values(promoted)])
        await api.registry.startEvaluation({ workspaceId: ws, modelVersionId: v, datasetVersionId: o.checkDatasetVersionId, pathFilter: filter, maxSamples: Number(max), confThreshold: Number(conf) });
    },
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["opt-evals", ws] }); onClose(); },
  });
  return (
    <Dialog title={t("Evaluate")} onClose={onClose}>
      <div className="space-y-3 text-sm">
        <Field label={t("Path filter")}><input className="input font-mono" value={filter} onChange={(e) => setFilter(e.target.value)} /></Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label={t("Max samples")}><input className="input" inputMode="numeric" value={max} onChange={(e) => setMax(e.target.value)} /></Field>
          <Field label={t("Confidence")}><input className="input" inputMode="decimal" value={conf} onChange={(e) => setConf(e.target.value)} /></Field>
        </div>
        <div className="flex flex-wrap gap-1 text-xs">{["source", ...Object.keys(promoted)].map((k) => <span key={k} className="rounded bg-surface-2 px-2 py-0.5">{k === "source" ? "FP32 (PyTorch)" : LABEL[k]}</span>)}</div>
        <ErrorBox error={run.error ? errorText(run.error) : null} />
        <button className="btn-primary" disabled={run.isPending || Number(max) < 1} onClick={() => run.mutate()}><Gauge className="h-4 w-4" />{t("Evaluate")}</button>
      </div>
    </Dialog>
  );
}
