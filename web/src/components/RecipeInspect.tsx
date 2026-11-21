import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, CircleAlert, FileText, Hammer, LoaderCircle, Lock, RefreshCw } from "lucide-react";
import { api, errorText } from "../lib/api";
import { bytes, ts } from "../lib/format";
import { ErrorBox } from "./ui";
import { translate as t } from "../i18n";
import type { Environment, Inspection, ModelBlock } from "../gen/mlplatform/v1/catalog_pb";

/* Slice 2 of the recipe canvas: the environment of a machine, the CPU-only inspection of a recipe, and what the panes draw from it. */

export type Cfg = Record<string, unknown>;
const isObj = (v: unknown): v is Cfg => !!v && typeof v === "object" && !Array.isArray(v);
export const parseObj = (s: string | undefined): Cfg | null => { try { const o: unknown = JSON.parse(s || ""); return isObj(o) ? o : null; } catch { return null; } };
/** Value at a dotted path ("train_dataloader.collate_fn.stop_epoch"); array elements by index. */
export const at = (o: unknown, path: string): unknown => path.split(".").reduce<unknown>((x, k) => (Array.isArray(x) ? x[Number(k)] : isObj(x) ? x[k] : undefined), o);
// YAML reads "1e-4" (no dot) as a string, so numbers may arrive as text.
const num = (v: unknown): number | undefined => (typeof v === "number" && Number.isFinite(v) ? v : typeof v === "string" && v.trim() !== "" && Number.isFinite(Number(v)) ? Number(v) : undefined);
const scalar = (v: unknown) => (typeof v === "string" ? v : JSON.stringify(v) ?? "null");
export const fmtNum = (v: number) => String(Number(v.toPrecision(3)));
const millions = (n: number) => `${(n / 1e6).toFixed(1)}M`;

/** One line per leaf, keyed by its dotted path; a list of plain values stays on one line. */
export function flatten(o: unknown, prefix = "", out: Record<string, string> = {}): Record<string, string> {
  const list = Array.isArray(o) && o.some((v) => v && typeof v === "object") ? o.map((v, i) => [String(i), v] as const) : isObj(o) ? Object.entries(o) : null;
  if (list && list.length > 0) for (const [k, v] of list) flatten(v, prefix ? `${prefix}.${k}` : k, out);
  else if (prefix) out[prefix] = scalar(o);
  return out;
}
export function configDiff(before: Cfg, after: Cfg): { key: string; from: string; to: string }[] {
  const a = flatten(before), b = flatten(after);
  return [...new Set([...Object.keys(a), ...Object.keys(b)])].filter((k) => a[k] !== b[k]).sort().map((k) => ({ key: k, from: a[k] ?? "—", to: b[k] ?? "—" }));
}

export interface Facts {
  epochs?: number; lr?: number; backboneLr?: number; warmup?: number; batch?: number; dropLast: boolean; milestones: number[]; gamma: number;
  imgSize?: [number, number]; multiscale: boolean; ops: { type: string; stage1Only: boolean }[]; checkpointFreq?: number;
  optimizer?: string; weightDecay?: number; ema: boolean; emaDecay?: number; clip?: number; lossWeights: [string, string][];
}
/** What the panes read from the merged config (key names of the DETR family; a missing key leaves its line out). */
export function facts(cfg: Cfg): Facts {
  const groups = at(cfg, "optimizer.params"), tf = "train_dataloader.dataset.transforms";
  const backbone = (Array.isArray(groups) ? groups : []).find((g) => isObj(g) && typeof g.params === "string" && g.params.includes("backbone") && num(g.lr) !== undefined) as Cfg | undefined;
  const rawOps = at(cfg, `${tf}.ops`), policyOps = at(cfg, `${tf}.policy.ops`);
  const stops = new Set(Array.isArray(policyOps) ? policyOps.map(String) : []);
  const ops = (Array.isArray(rawOps) ? rawOps : []).filter(isObj).map((o) => ({ raw: o, type: String(o.type ?? "?") }));
  const size = ops.find((o) => o.type === "Resize")?.raw.size ?? cfg.eval_spatial_size;
  const side = Array.isArray(size) ? [num(size[0]), num(size[1] ?? size[0])] : [num(size), num(size)];
  const criterion = Object.keys(cfg).find((k) => /Criterion$/.test(k) && isObj(at(cfg, `${k}.weight_dict`)));
  const steps = at(cfg, "lr_scheduler.milestones");
  return {
    epochs: num(cfg.epochs) ?? num(cfg.epoches), lr: num(at(cfg, "optimizer.lr")), backboneLr: num(backbone?.lr), warmup: num(at(cfg, "lr_warmup_scheduler.warmup_duration")),
    batch: num(at(cfg, "train_dataloader.total_batch_size")), dropLast: at(cfg, "train_dataloader.drop_last") === true,
    milestones: at(cfg, "lr_scheduler.type") === "MultiStepLR" && Array.isArray(steps) ? steps.map(num).filter((x): x is number => x !== undefined) : [], gamma: num(at(cfg, "lr_scheduler.gamma")) ?? 0.1,
    imgSize: side[0] !== undefined && side[1] !== undefined ? [side[0], side[1]] : undefined, multiscale: (at(cfg, "train_dataloader.collate_fn.base_size_repeat") ?? null) !== null,
    ops: ops.map((o) => ({ type: o.type, stage1Only: stops.has(o.type) })), checkpointFreq: num(cfg.checkpoint_freq),
    optimizer: typeof at(cfg, "optimizer.type") === "string" ? String(at(cfg, "optimizer.type")) : undefined, weightDecay: num(at(cfg, "optimizer.weight_decay")),
    ema: cfg.use_ema === true, emaDecay: num(at(cfg, "ema.decay")), clip: num(cfg.clip_max_norm),
    lossWeights: criterion ? Object.entries(at(cfg, `${criterion}.weight_dict`) as Cfg).map(([k, v]) => [k, scalar(v)]) : [],
  };
}

export interface Phase { key: string; label?: string; untilKey?: string }
export interface Span { n: number; label: string; from: number; to: number }
/** The manifest's phases laid over the epochs: a phase ends at the value of its `untilKey` in the config; one that gets no epoch is left out. */
export function phaseSpans(phases: Phase[], cfg: Cfg, total: number): Span[] {
  const out: Span[] = [];
  let start = 0;
  phases.forEach((p, i) => {
    const end = Math.max(start, Math.min(total, (p.untilKey ? num(at(cfg, p.untilKey)) : undefined) ?? total));
    if (end > start) out.push({ n: i + 1, label: p.label ?? p.key, from: start + 1, to: end });
    start = end;
  });
  return out;
}

// ---- A. Environment ----

export function useEnvironments(ws: string, recipe: string) {
  return useQuery({
    queryKey: ["environments", ws, recipe], queryFn: () => api.catalog.listEnvironments({ workspaceId: ws, recipe }),
    refetchInterval: (q) => (q.state.data?.environments.some((e) => e.state === "building") ? 5_000 : 30_000),
  });
}
/** The chosen machine's environment; with "any machine", the one an inspection or a run would land on. */
export const envFor = (envs: Environment[], hostId: string) => (hostId ? envs.find((e) => e.hostId === hostId) : envs.find((e) => e.online && e.state === "ready") ?? envs.find((e) => e.online));

function useNow(on: boolean) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => { if (!on) return; setNow(Date.now()); const id = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(id); }, [on]);
  return now;
}
const elapsed = (ms: number) => { const s = Math.max(0, Math.floor(ms / 1000)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`; };

export function EnvLine({ env }: { env: Environment | undefined }) {
  const building = env?.state === "building", now = useNow(building), since = ts(env?.startedAt);
  if (!env) return null;
  if (env.state === "ready") return <div className="flex items-center gap-1 text-success"><Check className="h-3 w-3 shrink-0" />{t("environment ready")}</div>;
  if (building) return <div className="flex items-center gap-1 text-info"><LoaderCircle className="h-3 w-3 shrink-0 animate-spin" />{t("building environment… {elapsed}", { elapsed: since ? elapsed(now - since.getTime()) : "" })}</div>;
  if (env.state === "failed") return <div className="flex items-center gap-1 text-danger"><CircleAlert className="h-3 w-3 shrink-0" />{t("environment build failed")}</div>;
  return <div className="text-warning">{t("environment not built on this machine yet")}</div>;
}

export function EnvPanel({ ws, recipe, envs, error }: { ws: string; recipe: string; envs: Environment[]; error?: string }) {
  const qc = useQueryClient();
  const build = useMutation({
    mutationFn: (hostId: string) => api.catalog.buildEnvironment({ workspaceId: ws, recipe, hostId }),
    onSettled: () => void qc.invalidateQueries({ queryKey: ["environments", ws, recipe] }),
  });
  return (
    <div className="space-y-2">
      <div className="text-xs font-medium text-fg-muted">{t("Environment on each machine")}</div>
      <ErrorBox error={error || (build.error ? errorText(build.error) : null)} />
      {envs.length === 0 && !error && <div className="text-xs text-fg-subtle">{t("No machine is connected yet.")}</div>}
      <ul className="divide-y divide-line rounded-lg border border-line">
        {envs.map((e) => (
          <li key={e.hostId} className="space-y-1.5 px-3 py-2 text-xs">
            <div className="flex items-center justify-between gap-2">
              <span className="min-w-0 truncate text-sm font-medium">{e.hostName || e.hostId.slice(0, 8)}{!e.online && <span className="ml-1.5 text-xs font-normal text-fg-subtle">{t("offline")}</span>}</span>
              {e.state !== "ready" && e.state !== "building" && (
                <button className="btn-ghost h-7 shrink-0 px-2 text-xs" disabled={!e.online || build.isPending} onClick={() => build.mutate(e.hostId)}>
                  <Hammer className="h-3.5 w-3.5" />{build.isPending && build.variables === e.hostId ? t("Starting…") : t("Build environment")}
                </button>)}
            </div>
            <EnvLine env={e} />
            {e.state === "failed" && e.error && <pre className="max-h-40 overflow-auto whitespace-pre-wrap break-words rounded-md bg-surface-2 p-2 font-mono text-[11px] text-danger">{e.error}</pre>}
          </li>))}
      </ul>
    </div>
  );
}

// ---- B. Inspection ----

export interface InspectionView { insp: Inspection; cfg: Cfg; variant: string; params: Cfg; key: string }
export type InspectStatus = "none" | "running" | "ready" | "failed" | "no_environment" | "error";
interface InspectArgs { ws: string; recipe: string; variant: string; paramsJson: string; inspectKey: string; hostId: string; envReady: boolean }

/**
 * The inspection of the draft. It is asked for 1.5 s after the last change that affects it (`inspectKey`), started only when an environment
 * is ready, and polled every 3 s while it runs. `view` is the last finished one — `outdated` once the draft moved on from it.
 */
export function useInspection(a: InspectArgs) {
  const key = `${a.variant}|${a.hostId}|${a.inspectKey}`;
  const [settled, setSettled] = useState({ key, variant: a.variant, paramsJson: a.paramsJson, hostId: a.hostId });
  useEffect(() => {
    if (key === settled.key) return;
    const id = window.setTimeout(() => setSettled({ key, variant: a.variant, paramsJson: a.paramsJson, hostId: a.hostId }), settled.variant ? 1500 : 0);
    return () => window.clearTimeout(id);
  }, [key, settled.key]); // eslint-disable-line react-hooks/exhaustive-deps
  const started = useRef("");
  const q = useQuery({
    queryKey: ["inspection", a.ws, a.recipe, settled.key, a.envReady], enabled: !!settled.variant,
    queryFn: async () => {
      const id = `${settled.key}|${a.envReady}`, start = a.envReady && started.current !== id;
      const r = await api.catalog.getInspection({ workspaceId: a.ws, recipe: a.recipe, variant: settled.variant, paramsJson: settled.paramsJson, numClasses: 0, hostId: settled.hostId, start });
      if (start) started.current = id;
      return r;
    },
    refetchInterval: (x) => (x.state.data?.state === "running" ? 3_000 : false),
  });
  const [view, setView] = useState<InspectionView | null>(null);
  useEffect(() => {
    const cfg = q.data?.state === "ready" ? parseObj(q.data.configJson) : null;
    if (q.data && cfg) setView({ insp: q.data, cfg, variant: settled.variant, params: parseObj(settled.paramsJson) ?? {}, key: settled.key });
  }, [q.data]); // eslint-disable-line react-hooks/exhaustive-deps
  const s = q.data?.state, unreadable = s === "ready" && !parseObj(q.data?.configJson);
  const status: InspectStatus = !a.variant ? "none"
    : key !== settled.key || (!q.data && !q.error) ? (a.envReady ? "running" : "no_environment")
    : q.error ? "error" : unreadable ? "failed"
    : s === "ready" || s === "failed" || s === "running" ? s
    : s === "no_environment" || !a.envReady ? "no_environment" : "none";
  return {
    view, outdated: !!view && view.key !== key, status,
    error: q.error ? errorText(q.error) : unreadable ? t("The inspection returned a config that cannot be read.") : q.data?.error ?? "",
    retry: () => { started.current = ""; void q.refetch(); },
  };
}

/** The inspection of the base revision, only if the server already has it (never started from here). */
export function useBaseInspection(a: { ws: string; recipe: string; variant: string; paramsJson: string; hostId: string; enabled: boolean }) {
  const q = useQuery({
    queryKey: ["inspection-base", a.ws, a.recipe, a.variant, a.paramsJson, a.hostId], enabled: a.enabled && !!a.variant,
    queryFn: () => api.catalog.getInspection({ workspaceId: a.ws, recipe: a.recipe, variant: a.variant, paramsJson: a.paramsJson, numClasses: 0, hostId: a.hostId, start: false }),
  });
  return useMemo(() => (q.data?.state === "ready" ? parseObj(q.data.configJson) : null), [q.data]);
}

export const Outdated = () => <span className="badge shrink-0 bg-amber-100 text-amber-700">{t("outdated")}</span>;

export function InspectLine({ status, error, onOpenEnv, onRetry }: { status: InspectStatus; error: string; onOpenEnv: () => void; onRetry: () => void }) {
  const again = <button type="button" className="inline-flex items-center gap-1 text-primary" onClick={onRetry}><RefreshCw className="h-3 w-3" />{t("Inspect again")}</button>;
  return (
    <div className="mt-2 flex flex-wrap items-start gap-x-2 gap-y-1 text-xs">
      {status === "running" ? <span className="inline-flex items-center gap-1 text-info"><LoaderCircle className="h-3 w-3 animate-spin" />{t("inspecting the recipe…")}</span>
        : status === "ready" ? <span className="inline-flex items-center gap-1 text-success"><Check className="h-3 w-3" />{t("inspected")}</span>
        : status === "no_environment" ? <><span className="text-warning">{t("not inspected: build the environment first")}</span><button type="button" className="text-primary" onClick={onOpenEnv}>{t("Open Environment & machine")}</button></>
        : status === "none" ? <><span className="text-fg-subtle">{t("not inspected yet")}</span>{again}</>
        : <><span className="min-w-0 whitespace-pre-wrap break-words text-danger">{error || t("inspection failed")}</span>{again}</>}
    </div>
  );
}

// ---- C. What the panes draw ----

const SPAN_COLORS = ["bg-primary", "bg-accent", "bg-info", "bg-success"];
export const warmupText = (steps: number, perEpoch: number | undefined) =>
  perEpoch ? t("warm-up {steps} steps ≈ {x} epochs with this data", { steps, x: (steps / perEpoch).toFixed(1) }) : t("warm-up {steps} steps", { steps });

/** The epochs as a strip of the recipe's stages, with the warm-up marked at its start. */
export function ScheduleStrip({ spans, total, warmup, perEpoch }: { spans: Span[]; total: number; warmup?: number; perEpoch?: number }) {
  const warm = warmup && perEpoch ? Math.min(1, warmup / perEpoch / total) : 0;
  return (
    <div>
      <div className="relative flex h-2 overflow-hidden rounded-full bg-surface-2">
        {spans.map((s, i) => <div key={s.n} className={SPAN_COLORS[i % SPAN_COLORS.length]} style={{ width: `${((s.to - s.from + 1) / total) * 100}%` }} />)}
        {warm > 0 && <div className="absolute inset-y-0 left-0 bg-warning" style={{ width: `${Math.max(1.5, warm * 100)}%` }} />}
      </div>
      <div className="mt-1 space-y-0.5 text-[11px] text-fg-muted">
        {spans.map((s, i) => (
          <div key={s.n} className="flex items-start gap-1"><span className={`mt-1 h-1.5 w-1.5 shrink-0 rounded-full ${SPAN_COLORS[i % SPAN_COLORS.length]}`} />
            <span>{t("stage {i}: epochs {from}–{to}", { i: s.n, from: s.from, to: s.to })} · {t(s.label)}</span></div>))}
        {warmup !== undefined && <div className="flex items-start gap-1"><span className="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-warning" /><span>{warmupText(warmup, perEpoch)}</span></div>}
      </div>
    </div>
  );
}

/**
 * Learning rate over the run, from the config alone: a linear rise over the warm-up steps, then flat (a MultiStepLR milestone inside the run
 * shows as a drop). The horizontal axis is epochs when the steps per epoch are known, otherwise the first steps of the run.
 */
export function LrChart({ f, total, perEpoch, spans }: { f: Facts; total: number; perEpoch?: number; spans: Span[] }) {
  if (f.lr === undefined) return null;
  const W = 320, H = 110, top = 6, bottom = 16, warm = Math.max(1, f.warmup ?? 1);
  const xmax = perEpoch ? total * perEpoch : warm * 4, ymax = Math.max(f.lr, f.backboneLr ?? 0);
  const x = (step: number) => (Math.min(step, xmax) / xmax) * W, y = (lr: number) => top + (1 - lr / ymax) * (H - top - bottom);
  const line = (peak: number) => {
    const pts: [number, number][] = [[0, peak / warm], [Math.min(warm, xmax), peak * Math.min(1, xmax / warm)]];
    let now = peak;
    if (perEpoch) for (const m of f.milestones.filter((e) => e < total && e * perEpoch > warm).sort((p, q) => p - q)) { pts.push([m * perEpoch, now]); now *= f.gamma; pts.push([m * perEpoch, now]); }
    if (warm < xmax) pts.push([xmax, now]);
    return pts.map(([s, lr]) => `${x(s).toFixed(1)},${y(lr).toFixed(1)}`).join(" ");
  };
  const backbone = f.backboneLr !== undefined && f.backboneLr !== f.lr ? f.backboneLr : undefined;
  return (
    <div>
      <svg viewBox={`0 0 ${W} ${H}`} className="w-full" role="img" aria-label={t("Learning rate over the run")}>
        <line x1={0} x2={W} y1={H - bottom} y2={H - bottom} className="stroke-line" strokeWidth={1} />
        {perEpoch && spans.slice(0, -1).map((s) => <line key={s.n} x1={x(s.to * perEpoch)} x2={x(s.to * perEpoch)} y1={top} y2={H - bottom} className="stroke-fg-subtle" strokeWidth={1} strokeDasharray="3 3" />)}
        {backbone !== undefined && <polyline points={line(backbone)} fill="none" className="stroke-accent" strokeWidth={1.5} />}
        <polyline points={line(f.lr)} fill="none" className="stroke-primary" strokeWidth={1.5} />
        <text x={0} y={H - 4} fontSize={9} className="fill-fg-subtle">0</text>
        <text x={W} y={H - 4} fontSize={9} textAnchor="end" className="fill-fg-subtle">{perEpoch ? t("epoch {n}", { n: total }) : t("step {n}", { n: xmax })}</text>
      </svg>
      <div className="mt-1 space-y-0.5 text-[11px] text-fg-muted">
        <div className="flex items-center gap-1"><span className="h-1.5 w-1.5 rounded-full bg-primary" />{t("main group · peak {lr}", { lr: fmtNum(f.lr) })}</div>
        {backbone !== undefined ? <div className="flex items-center gap-1"><span className="h-1.5 w-1.5 rounded-full bg-accent" />{t("backbone group · peak {lr}", { lr: fmtNum(backbone) })}</div> : null}
      </div>
    </div>
  );
}

/** The model's blocks: size and whether they are trained. `slower` = main LR ÷ backbone LR when the backbone learns slower. */
export function Blocks({ blocks, labels, slower, compact }: { blocks: ModelBlock[]; labels: Record<string, string | undefined>; slower?: number; compact?: boolean }) {
  const total = blocks.reduce((s, b) => s + Number(b.params), 0), trainable = blocks.reduce((s, b) => s + Number(b.trainable), 0);
  if (blocks.length === 0) return <div className="text-[11px] text-fg-subtle">{t("the inspection lists no blocks")}</div>;
  return (
    <div className={compact ? "space-y-0.5 text-[11px]" : "space-y-1 text-xs"}>
      {blocks.map((b) => {
        // A handful of fixed tensors (e.g. a denoising embedding) is not "partly trained": call it partial only below 99 %.
        const frozen = b.trainable === 0n, partly = !frozen && Number(b.trainable) < Number(b.params) * 0.99;
        return (
          <div key={b.key} className="flex items-center justify-between gap-2">
            <span className="min-w-0 truncate">{t(b.label || labels[b.key] || b.key)} <span className="tabular-nums text-fg-subtle">{millions(Number(b.params))}</span></span>
            <span className={`inline-flex shrink-0 items-center gap-1 ${frozen ? "text-info" : partly ? "text-warning" : "text-fg-muted"}`}>
              {frozen && <Lock className="h-3 w-3" />}{frozen ? t("frozen") : partly ? t("partly trained") : b.key === "backbone" && slower ? t("learns slower ×{ratio}", { ratio: fmtNum(slower) }) : t("trained")}
            </span>
          </div>);
      })}
      <div className="pt-0.5 text-fg-subtle">{t("{trainable} of {total} parameters are trained", { trainable: millions(trainable), total: millions(total) })}</div>
    </div>
  );
}

const PRESETS = ["Little data — freeze the backbone", "Balanced — the variant's defaults", "Lots of data — train everything at full speed"];
/** Three starting points for "what is trained"; `active` is the one the draft matches now (-1 = none). */
export function Presets({ active, onPick }: { active: number; onPick: (i: number) => void }) {
  return (
    <div className="space-y-1.5">
      {PRESETS.map((p, i) => (
        <button key={p} type="button" aria-pressed={active === i} onClick={() => onPick(i)}
          className={`block w-full rounded-lg border px-3 py-2 text-left text-sm ${active === i ? "border-primary bg-primary/10" : "border-line hover:border-fg-subtle"}`}>{t(p)}</button>))}
    </div>
  );
}

export function ObjectiveTable({ f }: { f: Facts }) {
  const rows: [string, string][] = [
    ...(f.optimizer ? [[t("Optimizer"), f.optimizer] as [string, string]] : []),
    ...(f.weightDecay !== undefined ? [[t("Weight decay"), fmtNum(f.weightDecay)] as [string, string]] : []),
    [t("EMA"), f.ema ? `${t("on")}${f.emaDecay !== undefined ? ` · ${t("decay {n}", { n: f.emaDecay })}` : ""}` : t("off")],
    ...(f.clip !== undefined ? [[t("Gradient clipping"), fmtNum(f.clip)] as [string, string]] : []),
    ...f.lossWeights.map(([k, v]) => [`${t("Loss weight")} · ${k}`, v] as [string, string])];
  return (
    <table className="w-full text-xs"><tbody>
      {rows.map(([k, v]) => <tr key={k} className="border-b border-line last:border-0"><td className="py-1 pr-2 text-fg-muted">{k}</td><td className="py-1 text-right font-mono tabular-nums">{v}</td></tr>)}
    </tbody></table>
  );
}

export const batchLine = (f: Facts) => [f.imgSize ? t("image {w}×{h}", { w: f.imgSize[1], h: f.imgSize[0] }) : "", f.multiscale ? t("multi-scale on") : t("multi-scale off")].filter(Boolean).join(" · ");
export function AugList({ f }: { f: Facts }) {
  return (
    <div className="space-y-2 text-xs">
      <div className="text-fg-muted">{batchLine(f)}</div>
      {f.ops.length > 0 && (
        <ol className="divide-y divide-line rounded-lg border border-line">
          {f.ops.map((o, i) => (
            <li key={i} className="flex items-center justify-between gap-2 px-3 py-1.5"><span className="font-mono">{o.type}</span>
              {o.stage1Only && <span className="badge bg-surface-2 text-fg-muted">{t("stops in stage 2")}</span>}</li>))}
        </ol>)}
    </div>
  );
}
export const checkpointText = (f: Facts) => (f.checkpointFreq !== undefined ? t("every {n} epochs", { n: f.checkpointFreq }) : "");

// ---- D. Visual · Config · Code ----

export type Level = "visual" | "config" | "code";
// "Code" comes back when it shows the source of the pane itself (the list of every recipe file said nothing about the pane).
const LEVELS: [Level, string][] = [["visual", "Visual"], ["config", "Config"], ["code", "Code"]];
export function LevelSwitch({ value, onChange, code }: { value: Level; onChange: (l: Level) => void; code: boolean }) {
  return (
    <div className="mb-4 inline-flex rounded-lg border border-line p-0.5 text-xs" role="tablist">
      {LEVELS.filter(([k]) => k !== "code" || code).map(([k, label]) => (
        <button key={k} type="button" role="tab" aria-selected={value === k} onClick={() => onChange(k)}
          className={`rounded-md px-3 py-1 ${value === k ? "bg-primary/10 font-medium text-primary" : "text-fg-muted hover:text-fg"}`}>{t(label)}</button>))}
    </div>
  );
}

const PRE = "max-h-[28rem] overflow-auto rounded-lg border border-line bg-surface-2 p-2 font-mono text-[11px] leading-relaxed";
export function ConfigView({ view, outdated }: { view: InspectionView | null; outdated: boolean }) {
  const [filter, setFilter] = useState("");
  const merged = useMemo(() => (view ? JSON.stringify(view.cfg, null, 2) : ""), [view]);
  const flat = useMemo(() => (view ? Object.entries(flatten(view.cfg)) : []), [view]);
  if (!view) return null;
  const needle = filter.trim().toLowerCase(), hits = needle ? flat.filter(([k, v]) => `${k}: ${v}`.toLowerCase().includes(needle)) : [];
  return (
    <div className="space-y-3">
      {outdated && <div className="flex justify-end"><Outdated /></div>}
      <div>
        <div className="mb-1 text-xs font-medium text-fg-muted">{t("Overrides")}</div>
        <pre className={PRE}>{view.insp.overlayYaml || "—"}</pre>
      </div>
      <div>
        <div className="mb-1 text-xs font-medium text-fg-muted">{t("Merged config")}</div>
        <input className="input mb-2" value={filter} placeholder={t("Filter by key or value")} aria-label={t("Filter by key or value")} onChange={(e) => setFilter(e.target.value)} />
        {needle ? (hits.length === 0 ? <div className="text-xs text-fg-subtle">{t("No key matches the filter.")}</div>
          : <pre className={PRE}>{hits.map(([k, v]) => `${k}: ${v}`).join("\n")}</pre>) : <pre className={PRE}>{merged}</pre>}
      </div>
    </div>
  );
}

/** Recipe page "Full text" tab: the recipe's own files (manifest, Dockerfile, shared scripts), read-only. */
export function RecipeFilesView({ ws, recipe }: { ws: string; recipe: string }) {
  const files = useQuery({ queryKey: ["recipe-files", ws, recipe], queryFn: () => api.catalog.listRecipeFiles({ workspaceId: ws, recipe }) });
  const [path, setPath] = useState("");
  const file = useQuery({ queryKey: ["recipe-file", ws, recipe, path], queryFn: () => api.catalog.readRecipeFile({ workspaceId: ws, recipe, path }), enabled: !!path });
  const groups = useMemo(() => {
    const out = new Map<string, { path: string; name: string; size: bigint }[]>();
    for (const f of [...(files.data?.files ?? [])].sort((a, b) => a.path.localeCompare(b.path))) {
      const cut = f.path.lastIndexOf("/"), dir = cut < 0 ? "" : f.path.slice(0, cut);
      out.set(dir, [...(out.get(dir) ?? []), { path: f.path, name: f.path.slice(cut + 1), size: f.sizeBytes }]);
    }
    return [...out.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [files.data]);
  const lines = useMemo(() => (file.data?.text ?? "").split("\n"), [file.data]);
  return (
    <div className="space-y-3">
      <ErrorBox error={files.error ? errorText(files.error) : null} onRetry={() => void files.refetch()} />
      {files.isLoading ? <div className="skeleton h-24" /> : groups.length === 0 && !files.error ? <div className="text-xs text-fg-subtle">{t("The recipe lists no files.")}</div> : (
        <div className="max-h-56 overflow-y-auto rounded-lg border border-line py-1">
          {groups.map(([dir, list]) => (
            <div key={dir}>
              {dir && <div className="px-3 pt-1.5 font-mono text-[11px] text-fg-subtle">{dir}/</div>}
              {list.map((f) => (
                <button key={f.path} type="button" aria-pressed={path === f.path} onClick={() => setPath(f.path)}
                  className={`flex w-full items-center gap-1.5 py-1 pr-3 text-left text-xs ${dir ? "pl-6" : "pl-3"} ${path === f.path ? "bg-primary/10 text-primary" : "hover:bg-surface-2"}`}>
                  <FileText className="h-3 w-3 shrink-0 text-fg-subtle" /><span className="min-w-0 flex-1 truncate font-mono">{f.name}</span><span className="shrink-0 text-[11px] text-fg-subtle">{bytes(f.size)}</span>
                </button>))}
            </div>))}
        </div>)}
      {path && (
        <div>
          <div className="mb-1 truncate font-mono text-xs text-fg-muted">{path}</div>
          <ErrorBox error={file.error ? errorText(file.error) : null} onRetry={() => void file.refetch()} />
          {file.isLoading ? <div className="skeleton h-32" /> : file.data && (
            <>
              <pre className={PRE}>{lines.map((l, i) => <div key={i} className="flex"><span className="mr-3 w-8 shrink-0 select-none text-right text-fg-subtle">{i + 1}</span><span className="whitespace-pre">{l}</span></div>)}</pre>
              {file.data.truncated && <div className="mt-1 text-xs text-warning">{t("The file is long — only its beginning is shown.")}</div>}
            </>)}
        </div>)}
    </div>
  );
}

/** One piece of upstream code the merged config selects for a pane (from the inspection). */
export interface PaneSource { name: string; file: string; line: number; code: string }
/** The code of each canvas pane, as the inspection reported it; empty when the recipe's inspection gives none. */
export function paneSources(insp: Inspection | undefined, pane: string): PaneSource[] {
  const all = parseObj(insp?.sourcesJson);
  const list = all?.[pane];
  return Array.isArray(list) ? (list as PaneSource[]).filter((s) => s && typeof s.code === "string") : [];
}

/** Code level of a pane: the upstream classes and functions this pane's settings run through, with their file and line. */
export function CodeView({ sources, outdated }: { sources: PaneSource[]; outdated: boolean }) {
  const [open, setOpen] = useState(0);
  const cur = sources[Math.min(open, sources.length - 1)];
  return (
    <div className="space-y-3">
      {outdated && <div className="flex justify-end"><Outdated /></div>}
      <div className="flex flex-wrap gap-1">
        {sources.map((s, i) => (
          <button key={s.name} type="button" aria-pressed={cur === s} onClick={() => setOpen(i)}
            className={`inline-flex items-center gap-1.5 rounded-md border px-2 py-1 font-mono text-xs ${cur === s ? "border-primary bg-primary/10 text-primary" : "border-line text-fg-muted hover:text-fg"}`}>
            <FileText className="h-3 w-3" />{s.name}
          </button>))}
      </div>
      {cur && (
        <div>
          <div className="mb-1 truncate font-mono text-xs text-fg-muted">{cur.file}:{cur.line}</div>
          <pre className={PRE}>{cur.code.replace(/\n$/, "").split("\n").map((l, i) => (
            <div key={i} className="flex"><span className="mr-3 w-10 shrink-0 select-none text-right text-fg-subtle">{cur.line + i}</span><span className="whitespace-pre">{l}</span></div>))}</pre>
        </div>)}
    </div>
  );
}
