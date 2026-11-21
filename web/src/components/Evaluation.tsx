import { useState } from "react";
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, Gauge, Loader2, Play, Square } from "lucide-react";
import { api, errorText } from "../lib/api";
import { Dialog, ErrorBox, Field, RelTime, StateBadge } from "./ui";
import { translate as t } from "../i18n";
import type { Evaluation, EvaluationSample } from "../gen/mlplatform/v1/registry_pb";

export type EvalTarget = { versionId: string; title: string; task: string };

const PALETTE = ["#f43f5e", "#3b82f6", "#f59e0b", "#a855f7", "#14b8a6", "#ec4899", "#eab308", "#06b6d4", "#84cc16"];
const GT_COLOR = "#22c55e";
const pct = (x: unknown) => (typeof x === "number" ? (x * 100).toFixed(1) + "%" : "—");
const parse = <T,>(s: string, fallback: T): T => { try { return s ? (JSON.parse(s) as T) : fallback; } catch { return fallback; } };

/** S-11-6 Evaluate a model version on a dataset version: start, follow progress, read the metrics, browse the worst samples. */
export function EvaluateSheet({ ws, target, onClose }: { ws: string; target: EvalTarget; onClose: () => void }) {
  const qc = useQueryClient();
  const [selected, setSelected] = useState<string | null>(null);
  const list = useQuery({
    queryKey: ["evaluations", ws, target.versionId],
    queryFn: () => api.registry.listEvaluations({ workspaceId: ws, modelVersionId: target.versionId }),
    refetchInterval: (q) => (q.state.data?.evaluations.some((e) => ["queued", "preparing", "running"].includes(e.state)) ? 2500 : 15000),
  });
  const evals = list.data?.evaluations ?? [];
  const current = evals.find((e) => e.id === selected);
  return (
    <Dialog title={t("Evaluate {name}", { name: target.title })} onClose={onClose} xwide>
      {current
        ? <EvaluationDetail ws={ws} e={current} task={target.task} onBack={() => setSelected(null)} />
        : (
          <div className="space-y-6">
            <StartForm ws={ws} target={target} onStarted={(id) => { void qc.invalidateQueries({ queryKey: ["evaluations", ws, target.versionId] }); setSelected(id); }} />
            <section>
              <h3 className="mb-2 text-sm font-semibold">{t("Previous evaluations")}</h3>
              <ErrorBox error={list.error ? errorText(list.error) : null} />
              {evals.length === 0 && !list.isLoading && <div className="text-sm text-fg-muted">{t("No evaluations yet.")}</div>}
              <div className="space-y-2">
                {evals.map((e) => <EvaluationRowView key={e.id} e={e} onOpen={() => setSelected(e.id)} />)}
              </div>
            </section>
          </div>)}
    </Dialog>
  );
}

function StartForm({ ws, target, onStarted }: { ws: string; target: EvalTarget; onStarted: (id: string) => void }) {
  const datasets = useQuery({ queryKey: ["datasets", ws], queryFn: () => api.data.listDatasets({ workspaceId: ws, task: "" }) });
  const [ds, setDs] = useState("");
  const versions = useQuery({ queryKey: ["dversions", ws, ds], queryFn: () => api.data.listVersions({ workspaceId: ws, datasetId: ds }), enabled: !!ds });
  const [f, setF] = useState({ versionId: "", filter: "", max: 200, conf: 0.25 });
  const start = useMutation({
    mutationFn: () => api.registry.startEvaluation({ workspaceId: ws, modelVersionId: target.versionId, datasetVersionId: f.versionId,
      pathFilter: f.filter, maxSamples: f.max, confThreshold: f.conf }),
    onSuccess: (e) => onStarted(e.id),
  });
  const compatible = (datasets.data?.datasets ?? []).filter((d) => d.task === target.task || target.task.startsWith("zero-shot"));
  return (
    <section className="space-y-3 rounded-lg border border-line p-4">
      <h3 className="text-sm font-semibold">{t("New evaluation")}</h3>
      <div className="grid gap-3 md:grid-cols-2">
        <Field label="Dataset">
          <select className="input" value={ds} onChange={(e) => { setDs(e.target.value); setF({ ...f, versionId: "" }); }}>
            <option value="">{t("— pick a dataset —")}</option>
            {compatible.map((d) => <option key={d.id} value={d.id}>{d.name} · {d.task}</option>)}
          </select>
        </Field>
        <Field label="Version">
          <select className="input" value={f.versionId} onChange={(e) => setF({ ...f, versionId: e.target.value })} disabled={!ds}>
            <option value="">{t("— pick a version —")}</option>
            {(versions.data?.versions ?? []).map((v) => <option key={v.id} value={v.id}>v{v.number} · {String(v.fileCount)} file</option>)}
          </select>
        </Field>
        <Field label={t("Only paths containing")}>
          <input className="input" value={f.filter} onChange={(e) => setF({ ...f, filter: e.target.value })} placeholder="test/" />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label={t("Max samples")}><input className="input" type="number" min={1} max={5000} value={f.max} onChange={(e) => setF({ ...f, max: Number(e.target.value) })} /></Field>
          <Field label={t("Confidence threshold")}>
            <input className="input" type="number" min={0.01} max={0.99} step={0.05} value={f.conf} onChange={(e) => setF({ ...f, conf: Number(e.target.value) })} />
          </Field>
        </div>
      </div>
      {compatible.length === 0 && !datasets.isLoading && <div className="text-xs text-fg-muted">{t("No dataset with task {task}.", { task: target.task })}</div>}
      <ErrorBox error={start.error ? errorText(start.error) : null} />
      <button className="btn-primary" disabled={!f.versionId || start.isPending} onClick={() => start.mutate()}><Play className="h-4 w-4" />{t("Start evaluation")}</button>
    </section>
  );
}

function headline(e: Evaluation) {
  const m = parse<Record<string, number>>(e.metricsJson, {});
  if (m["test/mAP50-95"] !== undefined) return `mAP50-95 ${pct(m["test/mAP50-95"])} · mAP50 ${pct(m["test/mAP50"])}`;
  if (m["test/accuracy"] !== undefined) return `accuracy ${pct(m["test/accuracy"])} · F1 ${pct(m["test/f1_macro"])}`;
  return "";
}

function EvaluationRowView({ e, onOpen }: { e: Evaluation; onOpen: () => void }) {
  const running = ["queued", "preparing", "running"].includes(e.state);
  return (
    <button className="flex w-full items-center gap-3 rounded-lg border border-line px-3 py-2 text-left text-sm hover:border-primary" onClick={onOpen}>
      <Gauge className="h-4 w-4 shrink-0 text-fg-muted" />
      <StateBadge state={e.state} reason={e.stateReason || undefined} />
      <span className="min-w-0 flex-1 truncate">
        {e.state === "succeeded" ? <b>{headline(e)}</b> : running ? <Progress e={e} /> : <span className="text-fg-muted">{t(e.stateReason || e.state)}</span>}
      </span>
      <span className="text-xs text-fg-subtle">{e.pathFilter && <span className="mono mr-2">{e.pathFilter}</span>}<RelTime at={e.createdAt} /></span>
    </button>
  );
}

function Progress({ e }: { e: Evaluation }) {
  const p = e.total ? Math.round((100 * e.done) / e.total) : 0;
  return (
    <span className="inline-flex w-full items-center gap-2">
      <Loader2 className="h-3.5 w-3.5 animate-spin text-fg-muted" />
      {e.state === "running"
        ? <><span className="h-1.5 flex-1 rounded bg-surface-2"><span className="block h-1.5 rounded bg-primary" style={{ width: `${p}%` }} /></span>
            <span className="mono text-xs">{e.done}/{e.total}</span></>
        : <span className="text-xs text-fg-muted">{t(e.stateReason || "Waiting")}</span>}
    </span>
  );
}

function EvaluationDetail({ ws, e, task, onBack }: { ws: string; e: Evaluation; task: string; onBack: () => void }) {
  const qc = useQueryClient();
  const m = parse<Record<string, number>>(e.metricsJson, {});
  const perClass = parse<Record<string, number | string>[]>(e.perClassJson, []);
  const extra = parse<{ labels?: string[]; matrix?: number[][]; sweep?: { conf: number; precision: number; recall: number; f1: number }[] } | null>(e.confusionJson, null);
  const confusion = extra?.labels && extra.matrix ? { labels: extra.labels, matrix: extra.matrix } : null;
  const sweep = extra?.sweep ?? [];
  const bestConf = m["test/best_conf"];
  const detect = task === "detect" || task === "zero-shot-detect";
  const running = ["queued", "preparing", "running"].includes(e.state);
  const cancel = useMutation({ mutationFn: () => api.registry.cancelEvaluation({ workspaceId: ws, evaluationId: e.id }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["evaluations", ws] }) });
  const cards: [string, string][] = detect
    ? ([["mAP50-95", pct(m["test/mAP50-95"])], ["mAP50", pct(m["test/mAP50"])], ["Precision", pct(m["test/precision"])], ["Recall", pct(m["test/recall"])], ["F1", pct(m["test/f1"])],
       [t("Missed"), String(m["test/fn"] ?? "—")], [t("False alarms"), String(m["test/fp"] ?? "—")]] as [string, string][])
      .concat(bestConf !== undefined ? [[t("Best threshold"), `${bestConf} · F1 ${pct(m["test/best_f1"])}`] as [string, string]] : [])
    : [["Accuracy", pct(m["test/accuracy"])], ["F1 macro", pct(m["test/f1_macro"])]];
  return (
    <div className="space-y-5">
      <div className="flex items-center gap-3">
        <button className="btn-ghost h-8 px-2" onClick={onBack}><ChevronLeft className="h-4 w-4" />{t("Back")}</button>
        <StateBadge state={e.state} reason={e.stateReason || undefined} />
        {running && <span className="min-w-0 flex-1"><Progress e={e} /></span>}
        {running && <button className="btn-ghost ml-auto h-8 px-2 text-xs" onClick={() => cancel.mutate()}><Square className="h-3.5 w-3.5" />{t("Cancel")}</button>}
      </div>
      {e.state === "failed" && <ErrorBox error={t(e.stateReason)} />}
      {e.state === "succeeded" && (
        <>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4 lg:grid-cols-8">
            {cards.map(([k, v]) => (
              <div key={k} className="rounded-lg border border-line bg-surface p-3">
                <div className="text-xs text-fg-muted">{k}</div><div className="mono mt-1 text-lg font-semibold">{v}</div>
              </div>))}
          </div>
          <div className="text-xs text-fg-muted">
            {t("{n} samples · {ms} ms per call on average", { n: m["test/samples"] ?? 0, ms: m["test/latency_ms"] ?? 0 })}
            {e.errors > 0 && <> · <span className="text-rose-500">{t("{n} samples could not be evaluated", { n: e.errors })}</span></>}
          </div>
          {perClass.length > 0 && (
            <div className="overflow-x-auto rounded-lg border border-line">
              <table className="w-full text-sm">
                <thead className="bg-surface-2 text-left text-xs text-fg-muted">
                  <tr><th className="px-3 py-2">{t("Class")}</th><th>{t("Labels")}</th>
                    {detect ? <><th>AP50</th><th>AP50-95</th></> : <th>F1</th>}<th>Precision</th><th>Recall</th></tr>
                </thead>
                <tbody>
                  {perClass.map((c) => (
                    <tr key={String(c.class)} className="border-t border-line">
                      <td className="px-3 py-1.5 font-medium">{String(c.class)}</td><td className="mono text-xs">{c.gt}</td>
                      {detect ? <><td className="mono text-xs">{pct(c.ap50)}</td><td className="mono text-xs">{pct(c.ap50_95)}</td></> : <td className="mono text-xs">{pct(c.f1)}</td>}
                      <td className="mono text-xs">{pct(c.precision)}</td><td className="mono text-xs">{pct(c.recall)}</td>
                    </tr>))}
                </tbody>
              </table>
            </div>)}
          {confusion && <ConfusionMatrix c={confusion} />}
          {sweep.length > 0 && <ThresholdTable sweep={sweep} best={bestConf} current={e.confThreshold} />}
        </>)}
      {e.done > 0 && <SampleGallery ws={ws} e={e} detect={detect} initialConf={bestConf ?? e.confThreshold} />}
    </div>
  );
}

function ConfusionMatrix({ c }: { c: { labels: string[]; matrix: number[][] } }) {
  const max = Math.max(1, ...c.matrix.flat());
  return (
    <div className="overflow-x-auto">
      <div className="mb-1 text-xs text-fg-muted">{t("Rows: true class · columns: predicted class")}</div>
      <table className="text-xs">
        <thead><tr><th />{c.labels.map((l) => <th key={l} className="px-2 py-1 font-medium">{l}</th>)}</tr></thead>
        <tbody>
          {c.matrix.map((row, i) => (
            <tr key={i}><th className="px-2 py-1 text-right font-medium">{c.labels[i]}</th>
              {row.map((v, j) => (
                <td key={j} className="mono h-8 w-12 text-center" style={{ background: `rgba(${i === j ? "34,197,94" : "244,63,94"},${(v / max) * 0.6})` }}>{v || ""}</td>))}
            </tr>))}
        </tbody>
      </table>
    </div>
  );
}

function ThresholdTable({ sweep, best, current }: { sweep: { conf: number; precision: number; recall: number; f1: number }[]; best?: number; current: number }) {
  return (
    <div>
      <div className="flex items-end gap-1 overflow-x-auto pb-1">
        {sweep.map((x) => (
          <div key={x.conf} className={`flex w-10 shrink-0 flex-col items-center rounded px-0.5 py-1 text-[10px] ${x.conf === best ? "bg-primary/15 ring-1 ring-primary" : ""}`}
            title={`conf ${x.conf}: P ${pct(x.precision)} · R ${pct(x.recall)} · F1 ${pct(x.f1)}`}>
            <div className="flex h-16 items-end gap-px">
              <span className="w-1.5 rounded-t bg-sky-500" style={{ height: `${x.precision * 100}%` }} />
              <span className="w-1.5 rounded-t bg-amber-500" style={{ height: `${x.recall * 100}%` }} />
              <span className="w-1.5 rounded-t bg-emerald-500" style={{ height: `${x.f1 * 100}%` }} />
            </div>
            <span className={`mono mt-1 ${x.conf === current ? "font-bold" : ""}`}>{x.conf.toFixed(2)}</span>
          </div>))}
      </div>
      <div className="flex gap-3 text-[11px] text-fg-muted">
        <span className="inline-flex items-center gap-1"><span className="h-2 w-2 rounded-sm bg-sky-500" />Precision</span>
        <span className="inline-flex items-center gap-1"><span className="h-2 w-2 rounded-sm bg-amber-500" />Recall</span>
        <span className="inline-flex items-center gap-1"><span className="h-2 w-2 rounded-sm bg-emerald-500" />F1</span>
      </div>
    </div>
  );
}

function SampleGallery({ ws, e, detect, initialConf }: { ws: string; e: Evaluation; detect: boolean; initialConf: number }) {
  const [errorsOnly, setErrorsOnly] = useState(true);
  const [conf, setConf] = useState(initialConf);
  const page = 24;
  const samples = useInfiniteQuery({
    queryKey: ["eval-samples", ws, e.id, errorsOnly, e.state === "succeeded"],
    initialPageParam: 0,
    queryFn: ({ pageParam }) => api.registry.listEvaluationSamples({ workspaceId: ws, evaluationId: e.id, errorsOnly, offset: pageParam, limit: page }),
    getNextPageParam: (last, all) => (last.samples.length < page ? undefined : all.length * page),
  });
  const rows = samples.data?.pages.flatMap((p) => p.samples) ?? [];
  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">{t("Samples")} <span className="font-normal text-fg-muted">{t("(worst first)")}</span></h3>
        <span className="inline-flex items-center gap-4 text-xs">
          {detect && <span className="inline-flex items-center gap-2">{t("Show boxes from")}
            <input type="range" min={0.05} max={0.95} step={0.05} value={conf} onChange={(x) => setConf(Number(x.target.value))} className="w-32" />
            <span className="mono w-8">{conf.toFixed(2)}</span></span>}
          <label className="inline-flex items-center gap-2"><input type="checkbox" checked={errorsOnly} onChange={(x) => setErrorsOnly(x.target.checked)} />{t("Only mistakes")}</label>
        </span>
      </div>
      {detect && (
        <div className="flex gap-4 text-xs text-fg-muted">
          <span className="inline-flex items-center gap-1"><span className="inline-block h-0.5 w-4 border-t-2 border-dashed" style={{ borderColor: GT_COLOR }} />{t("label (ground truth)")}</span>
          <span className="inline-flex items-center gap-1"><span className="inline-block h-0.5 w-4" style={{ background: PALETTE[0] }} />{t("model prediction ≥ threshold")}</span>
        </div>)}
      {rows.length === 0 && !samples.isLoading && <div className="text-sm text-fg-muted">{errorsOnly ? t("No mistakes in the evaluated samples.") : t("No samples yet.")}</div>}
      <div className="grid gap-3 md:grid-cols-2">
        {rows.map((s) => <SampleView key={s.idx} s={s} detect={detect} conf={conf} />)}
      </div>
      {samples.hasNextPage && <button className="btn-ghost" onClick={() => void samples.fetchNextPage()} disabled={samples.isFetchingNextPage}>{t("Load more")}</button>}
    </section>
  );
}

type Box = { c: string; b: [number, number, number, number]; s?: number };

function SampleView({ s, detect, conf }: { s: EvaluationSample; detect: boolean; conf: number }) {
  const [size, setSize] = useState<{ w: number; h: number } | null>(null);
  const w = s.width || size?.w || 1;
  const h = s.height || size?.h || 1;
  const gt = detect ? parse<Box[]>(s.gtJson, []) : [];
  const pred = detect ? parse<Box[]>(s.predJson, []).filter((p) => (p.s ?? 0) >= conf) : [];
  const classes = [...new Set(pred.map((p) => p.c))];
  const cls = !detect ? { gt: parse<{ c?: string }>(s.gtJson, {}).c ?? "", pred: parse<{ c?: string; s?: number }>(s.predJson, {}) } : null;
  return (
    <div className="rounded-lg border border-line bg-surface p-2">
      <div className="relative">
        <img src={s.imageUrl} alt={s.path} loading="lazy" className="block w-full rounded" onLoad={(x) => setSize({ w: x.currentTarget.naturalWidth, h: x.currentTarget.naturalHeight })} />
        {detect && (
          <svg className="pointer-events-none absolute inset-0 h-full w-full" viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none">
            {gt.map((g, i) => <rect key={"g" + i} x={g.b[0]} y={g.b[1]} width={g.b[2] - g.b[0]} height={g.b[3] - g.b[1]} fill="none" stroke={GT_COLOR}
              strokeWidth={Math.max(2, w / 300)} strokeDasharray={`${w / 80} ${w / 120}`} />)}
            {pred.map((p, i) => {
              const c = PALETTE[classes.indexOf(p.c) % PALETTE.length];
              return (
                <g key={"p" + i}>
                  <rect x={p.b[0]} y={p.b[1]} width={p.b[2] - p.b[0]} height={p.b[3] - p.b[1]} fill="none" stroke={c} strokeWidth={Math.max(2, w / 300)} />
                  <text x={p.b[0]} y={Math.max(p.b[1] - 4, w / 50)} fill={c} fontSize={Math.max(12, w / 45)} fontWeight={600} paintOrder="stroke" stroke="#000" strokeWidth={Math.max(2, w / 300)}>
                    {p.c} {Math.round((p.s ?? 0) * 100)}%</text>
                </g>);
            })}
          </svg>)}
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-2 text-xs">
        <span className="mono min-w-0 flex-1 truncate text-fg-subtle" title={s.path}>{s.path}</span>
        {s.error ? <span className="text-rose-500">{t(s.error)}</span> : detect ? (
          <>
            <span className="badge bg-emerald-100 text-emerald-700">{t("hit")} {s.tp}</span>
            {s.fn > 0 && <span className="badge bg-amber-100 text-amber-700">{t("missed")} {s.fn}</span>}
            {s.fp > 0 && <span className="badge bg-rose-100 text-rose-700">{t("false alarm")} {s.fp}</span>}
          </>
        ) : cls && (
          <span className={cls.gt === cls.pred.c ? "text-emerald-500" : "text-rose-500"}>
            {t("label")}: <b>{cls.gt}</b> · {t("model")}: <b>{cls.pred.c}</b> {pct(cls.pred.s)}
          </span>)}
      </div>
    </div>
  );
}
