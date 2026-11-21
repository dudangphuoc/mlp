import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { TransferEstimate } from "./DataSync";
import { ParamBoard } from "./TrainParams";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Boxes, ChevronRight, Circle, CircleAlert, CircleCheck, CircleX, Cpu, Database, FileText, FlaskConical, Gauge, Minus, Package, Pencil, Play, PlayCircle,
  Repeat, RotateCcw, Square, TriangleAlert, Undo2, type LucideIcon,
} from "lucide-react";
import { api, errorText, isAborted } from "../lib/api";
import { bytes } from "../lib/format";
import { Dialog, ErrorBox, Field } from "./ui";
import { translate as t } from "../i18n";
import { useSticky, writeSticky } from "../lib/persist";
import type { Draft, DraftChange, DraftState, ProjectRecipe, RecipeTrial, Run, TrialStage } from "../gen/mlplatform/v1/tracking_pb";
import type { DatasetVersion } from "../gen/mlplatform/v1/data_pb";
import {
  AugList, Blocks, CodeView, ConfigView, paneSources, EnvLine, EnvPanel, InspectLine, LevelSwitch, LrChart, ObjectiveTable, Outdated, Presets, ScheduleStrip,
  batchLine, checkpointText, configDiff, envFor, facts, fmtNum, phaseSpans, useBaseInspection, useEnvironments, useInspection, type Level, type Phase,
} from "./RecipeInspect";

export interface ParamDef {
  type?: string | string[]; default?: unknown; enum?: unknown[]; description?: string; from?: string;
  minimum?: number; maximum?: number; exclusiveMinimum?: number; multipleOf?: number; pane?: string; label?: string; help?: string; affectsInspect?: boolean;
}
type Raw = Record<string, string | boolean>;
interface ManifestVariant { name: string; displayName?: string; params?: number; reported?: Record<string, number>; tasks?: string[] }
interface Manifest {
  params?: Record<string, ParamDef>; variants?: ManifestVariant[]; canvas?: { phases?: Phase[]; blocks?: { key: string; label?: string }[] }; environment?: { gpu?: string };
  entrypoints?: { train?: { fromScratch?: boolean } };
  artifacts?: { best?: { from?: string | string[]; as?: string }; files?: { from: string; to?: string }[] };
}

const kind = (d: ParamDef) => (Array.isArray(d.type) ? d.type[0] : d.type) ?? "string";
const parseJson = <T,>(s: string | undefined, fallback: T): T => { try { const o = JSON.parse(s || ""); return o && typeof o === "object" ? (o as T) : fallback; } catch { return fallback; } };

/** Same rules as the server (TrainParams.Validate) so a wrong value is flagged while typing, not after the job was queued. */
export function checkParam(d: ParamDef, raw: string | boolean | undefined): string | null {
  if (raw === undefined || raw === "") return null;   // empty = the recipe default
  const k = kind(d);
  if (d.enum) return d.enum.some((o) => String(o) === String(raw)) ? null : t("Pick one of the listed values");
  if (k === "integer" || k === "number") {
    const n = Number(raw);
    if (typeof raw !== "string" || raw.trim() === "" || !Number.isFinite(n)) return t("Must be a number");
    if (k === "integer" && !Number.isInteger(n)) return t("Must be a whole number");
    if (d.minimum !== undefined && n < d.minimum) return t("Must be at least {n}", { n: d.minimum });
    if (d.exclusiveMinimum !== undefined && n <= d.exclusiveMinimum) return t("Must be greater than {n}", { n: d.exclusiveMinimum });
    if (d.maximum !== undefined && n > d.maximum) return t("Must be at most {n}", { n: d.maximum });
    if (d.multipleOf && Math.abs(n / d.multipleOf - Math.round(n / d.multipleOf)) > 1e-9) return t("Must be a multiple of {n}", { n: d.multipleOf });
  }
  return null;
}

function toValue(d: ParamDef, raw: string | boolean): unknown {
  const k = kind(d);
  if (k === "boolean") return raw === true || raw === "true";
  if (k === "integer" || k === "number") return Number(raw);
  return raw;
}
const toRaw = (json: string | undefined): Raw => Object.fromEntries(Object.entries(parseJson<Record<string, unknown>>(json, {})).map(([k, v]) => [k, typeof v === "boolean" ? v : String(v)]));
const noDefault = (d: ParamDef) => d.default === undefined || d.default === null;
/** Only what the person set away from the recipe default; keys the manifest does not declare are kept as they were. */
function paramsJson(defs: [string, ParamDef][], raw: Raw, before: string | undefined): string {
  const known = new Set(defs.map(([k]) => k));
  const out: Record<string, unknown> = Object.fromEntries(Object.entries(parseJson<Record<string, unknown>>(before, {})).filter(([k]) => !known.has(k)));
  for (const [k, d] of defs) {
    const v = raw[k];
    if (v === undefined || v === "" || checkParam(d, v)) continue;
    const value = toValue(d, v);
    if (noDefault(d) || String(value) !== String(d.default)) out[k] = value;
  }
  return JSON.stringify(out);
}
/** `real` = the value the inspected config has for a parameter the recipe leaves to the variant (default: null). */
const shown = (d: ParamDef, v: string | boolean | undefined, real?: string) => {
  const x = v === undefined || v === "" ? d.default : v;
  return x === undefined || x === null ? real ?? "—" : kind(d) === "boolean" ? (x === true || x === "true" ? t("on") : t("off")) : String(x);
};

type PaneKey = "batch" | "learn" | "objective" | "schedule" | "validation" | "checkpoint";
type Sel = "machine" | "data" | "model" | "package" | "evaluate" | PaneKey;
const PANES: { key: PaneKey; label: string; each?: boolean }[] = [
  { key: "batch", label: "Batch & augmentation" }, { key: "learn", label: "What is trained" }, { key: "objective", label: "Objective & optimizer" },
  { key: "schedule", label: "Training schedule" }, { key: "validation", label: "Validation", each: true }, { key: "checkpoint", label: "Checkpoint", each: true }];
const TITLES: Record<Exclude<Sel, PaneKey>, string> = { machine: "Environment & machine", data: "Data", model: "Model & starting point", package: "Package", evaluate: "Evaluate" };
const DEFAULT_PANE: Record<string, PaneKey> = { batch: "batch", epochs: "schedule" };
const paneOf = (k: string, d: ParamDef): PaneKey => (PANES.some((p) => p.key === d.pane) ? (d.pane as PaneKey) : DEFAULT_PANE[k] ?? "objective");
const hasSplit = (s: string) => s === "folders" || s === "file";
const stickyKey = (projectId: string) => `project:${projectId}:recipe`;

/** R5 "Train again" from a finished run: same revision and start, another dataset version — the canvas opens locked on #n's recipe. */
export async function retrainFrom(ws: string, run: Run): Promise<ProjectRecipe> {
  const pr = await api.tracking.adoptRecipe({ workspaceId: ws, projectId: run.projectId, recipe: run.recipe });
  await api.tracking.resetDraft({ workspaceId: ws, projectRecipeId: pr.id, runId: run.id, retrain: true });
  writeSticky(stickyKey(run.projectId), pr.id);
  return pr;
}

/** "Cook again from #n": the project's copy of the run's recipe, with the caller's draft set to what that run trained. */
export async function cookAgain(ws: string, run: Run): Promise<ProjectRecipe> {
  const pr = await api.tracking.adoptRecipe({ workspaceId: ws, projectId: run.projectId, recipe: run.recipe });
  await api.tracking.resetDraft({ workspaceId: ws, projectRecipeId: pr.id, runId: run.id });
  writeSticky(stickyKey(run.projectId), pr.id);
  return pr;
}


/**
 * The caller's draft of one project recipe. Edits are local at once and saved 800 ms after the last one with the newest etag;
 * `epoch` moves whenever the server copy replaces the local one (first load, reset, reload after another tab saved).
 */
function useDraft(ws: string, prId: string) {
  const [state, setState] = useState<DraftState | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [epoch, setEpoch] = useState(0);
  const [status, setStatus] = useState<"" | "saving" | "saved">("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const ref = useRef<{ etag: string; draft: Draft | null; state: DraftState | null; dirty: boolean; timer: number; busy: Promise<boolean> | null }>(
    { etag: "", draft: null, state: null, dirty: false, timer: 0, busy: null });

  const replace = (s: DraftState) => {
    const r = ref.current;
    window.clearTimeout(r.timer);
    r.etag = s.etag; r.state = s; r.draft = s.draft ?? null; r.dirty = false;
    setState(s); setDraft(r.draft); setEpoch((e) => e + 1); setStatus(""); setError("");
  };
  const load = () => api.tracking.getDraft({ workspaceId: ws, projectRecipeId: prId }).then(replace, (e) => setError(errorText(e)));
  const flush = async (): Promise<boolean> => {
    const r = ref.current;
    window.clearTimeout(r.timer);
    while (r.busy) await r.busy;
    if (!r.dirty || !r.draft) return true;
    r.dirty = false; setStatus("saving");
    const sent = r.draft;
    r.busy = (async () => {
      try {
        const s = await api.tracking.updateDraft({ workspaceId: ws, draft: sent, etag: r.etag });
        r.etag = s.etag; r.state = s; setState(s); setError("");
        if (!r.dirty) setStatus("saved");
        return true;
      } catch (e) {
        if (isAborted(e)) { await load(); setNotice(t("This draft was changed in another tab — the latest version was loaded.")); }
        else { r.dirty = true; setStatus(""); setError(errorText(e)); }
        return false;
      } finally { r.busy = null; }
    })();
    return r.busy;
  };
  const edit = (patch: Partial<Draft>) => {
    const r = ref.current;
    if (!r.draft) return;
    r.draft = { ...r.draft, ...patch }; r.dirty = true;
    setDraft(r.draft); setNotice("");
    window.clearTimeout(r.timer);
    r.timer = window.setTimeout(() => void flush(), 800);
  };
  const reset = async (req: { revisionNumber?: number; origin?: boolean }) => {
    const r = ref.current;
    window.clearTimeout(r.timer); r.dirty = false;
    while (r.busy) await r.busy;
    replace(await api.tracking.resetDraft({ workspaceId: ws, projectRecipeId: prId, ...req }));
  };
  useEffect(() => { void load(); return () => { if (ref.current.dirty) void flush(); }; }, [ws, prId]); // eslint-disable-line react-hooks/exhaustive-deps
  return { state, draft, epoch, status, error, notice, edit, flush, reset, reload: load, gateOpen: () => !!ref.current.state?.gate?.canTrain };
}

const SPLITS = [["train", "bg-split-train"], ["val", "bg-split-val"], ["test", "bg-split-test"]] as const;
function SplitBar({ counts }: { counts: Record<string, bigint> }) {
  const n = SPLITS.map(([k]) => Number(counts[k] ?? 0)), total = n.reduce((a, b) => a + b, 0);
  if (total === 0) return null;
  return (
    <div>
      <div className="flex h-1.5 overflow-hidden rounded-full bg-surface-2">{SPLITS.map(([k, c], i) => n[i] > 0 && <div key={k} className={c} style={{ width: `${(n[i] / total) * 100}%` }} />)}</div>
      <div className="mt-1 flex flex-wrap gap-x-2 text-[11px] text-fg-muted">
        {SPLITS.map(([k, c], i) => <span key={k} className="inline-flex items-center gap-1"><span className={`h-1.5 w-1.5 rounded-full ${c}`} />{k} {n[i].toLocaleString()}</span>)}
      </div>
    </div>
  );
}

const Num = ({ n }: { n: number }) => <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-surface-2 text-[11px] font-semibold text-fg-muted">{n}</span>;
const Edited = () => <Pencil className="h-3 w-3 shrink-0 text-primary" aria-label={t("changed since the revision")} />;

function Node({ n, title, icon: Icon, active, edited, onClick, children }: { n: number; title: string; icon: LucideIcon; active: boolean; edited?: boolean; onClick: () => void; children?: ReactNode }) {
  return (
    <button type="button" aria-pressed={active} onClick={onClick}
      className={`card w-full p-3 text-left transition hover:border-primary/50 ${active ? "border-primary bg-primary/5" : ""}`}>
      <div className="flex items-center gap-2"><Num n={n} /><span className="min-w-0 flex-1 truncate text-sm font-semibold">{t(title)}</span>{edited && <Edited />}<Icon className="h-4 w-4 shrink-0 text-fg-subtle" /></div>
      {children && <div className="mt-2 space-y-1 text-xs text-fg-muted">{children}</div>}
    </button>
  );
}


function ParamField({ name, def, value, error, change, placeholder, onChange }: {
  name: string; def: ParamDef; value: string | boolean | undefined; error?: string; change?: DraftChange; placeholder?: string; onChange: (v: string | boolean | undefined) => void;
}) {
  const k = kind(def), on = value === undefined || value === "" ? Boolean(def.default) : value === true || value === "true";
  const empty = def.default === null ? placeholder ?? t("as the variant has it") : String(def.default ?? "");
  const back = () => onChange(!change || change.from === "" || (!noDefault(def) && change.from === String(def.default)) ? undefined : k === "boolean" ? change.from === "true" : change.from);
  return (
    <div>
      <div className="mb-1 flex items-center justify-between gap-2">
        <span className="flex min-w-0 items-center gap-1.5 text-xs font-medium text-fg-muted">
          {def.label && <span className="truncate">{t(def.label)}</span>}<span className={`font-mono ${def.label ? "font-normal text-fg-subtle" : ""}`}>{name}</span>{change && <Edited />}
        </span>
        {change && <button type="button" className="inline-flex shrink-0 items-center gap-1 text-xs text-primary" title={change.from || String(def.default ?? "")} onClick={back}><RotateCcw className="h-3 w-3" />{t("back to the previous value")}</button>}
      </div>
      {def.enum ? (
        <select className="input" aria-label={name} value={String(value ?? def.default ?? "")} onChange={(e) => onChange(e.target.value)}>
          {noDefault(def) && <option value="">{def.default === null ? t("as the variant has it") : "—"}</option>}
          {def.enum.map((o) => <option key={String(o)}>{String(o)}</option>)}
        </select>
      ) : k === "boolean" ? (
        <label className="flex h-[34px] items-center gap-2 text-sm"><input type="checkbox" aria-label={name} checked={on} onChange={(e) => onChange(e.target.checked)} />{on ? t("on") : t("off")}</label>
      ) : (
        <input className={`input ${error ? "border-danger focus:border-danger" : change ? "border-primary/60" : ""}`} inputMode={k === "integer" || k === "number" ? "decimal" : undefined}
          value={value === undefined ? String(def.default ?? "") : String(value)} placeholder={empty} onChange={(e) => onChange(e.target.value)} aria-invalid={!!error} aria-label={name} />)}
      {error && <div className="mt-1 text-xs text-danger">{error}</div>}
    </div>
  );
}

function SplitForm({ ws, versionId, onDone }: { ws: string; versionId: string; onDone: (v: DatasetVersion) => void }) {
  const [pct, setPct] = useState({ train: "80", val: "10", test: "10" });
  const [groupBy, setGroupBy] = useState<"file" | "folder">("file");
  const n = SPLITS.map(([k]) => Number(pct[k])), valid = n.every((x, i) => pct[SPLITS[i][0]].trim() !== "" && Number.isInteger(x) && x >= 0 && x <= 100), sum = n.reduce((a, b) => a + b, 0);
  const split = useMutation({ mutationFn: () => api.data.splitVersion({ workspaceId: ws, versionId, trainPct: n[0], valPct: n[1], testPct: n[2], groupBy }), onSuccess: onDone });
  return (
    <div className="rounded-lg border border-line p-3">
      <div className="flex items-start gap-2 text-xs text-warning"><TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" />{t("This version has no train/val split yet.")}</div>
      <div className="mt-3 text-sm font-medium">{t("Split into train / val / test")}</div>
      <div className="mt-2 grid grid-cols-3 gap-2">
        {SPLITS.map(([k]) => (
          <label key={k} className="block"><span className="label">{k} %</span>
            <input className="input" inputMode="numeric" aria-label={`${k} %`} value={pct[k]} onChange={(e) => setPct({ ...pct, [k]: e.target.value })} /></label>))}
      </div>
      {(!valid || sum !== 100) && <div className="mt-1 text-xs text-danger">{valid ? t("The three parts must add up to 100 (now {n})", { n: sum }) : t("Each part is a whole number from 0 to 100")}</div>}
      <div className="mt-3 space-y-1.5 text-sm">
        <label className="flex items-center gap-2"><input type="radio" name="split-group" checked={groupBy === "file"} onChange={() => setGroupBy("file")} />{t("By file")}</label>
        <label className="flex items-center gap-2"><input type="radio" name="split-group" checked={groupBy === "folder"} onChange={() => setGroupBy("folder")} />{t("By folder")}</label>
      </div>
      {split.error && <div className="mt-2"><ErrorBox error={errorText(split.error)} /></div>}
      <button className="btn-primary mt-3" disabled={!valid || sum !== 100 || split.isPending} onClick={() => split.mutate()}>{split.isPending ? t("Splitting…") : t("Split and save as a new version")}</button>
    </div>
  );
}

function DataPanel({ ws, task, datasetId, versionId, onDataset, onVersion, go }: {
  ws: string; task: string; datasetId: string; versionId: string; onDataset: (id: string) => void; onVersion: (id: string) => void; go: (path: string) => void;
}) {
  const qc = useQueryClient();
  const datasets = useQuery({ queryKey: ["datasets", ws, task], queryFn: () => api.data.listDatasets({ workspaceId: ws, task }) });
  const datasetList = datasets.data?.datasets ?? [];
  const versions = useQuery({ queryKey: ["versions", ws, datasetId], queryFn: () => api.data.listVersions({ workspaceId: ws, datasetId }), enabled: !!datasetId });
  const versionList = useMemo(() => [...(versions.data?.versions ?? [])].sort((a, b) => b.number - a.number), [versions.data]);
  const version = versionList.find((v) => v.id === versionId);
  useEffect(() => { if (!datasetId && datasetList.length === 1) onDataset(datasetList[0].id); }, [datasetList]); // eslint-disable-line react-hooks/exhaustive-deps
  // Newest version by default — training on an older one is a deliberate choice.
  useEffect(() => {
    if (datasetId && versions.data && !versionList.some((v) => v.id === versionId) && (versionList[0]?.id ?? "") !== versionId) onVersion(versionList[0]?.id ?? "");
  }, [datasetId, versions.data]); // eslint-disable-line react-hooks/exhaustive-deps

  if (datasets.isLoading) return <div className="skeleton h-24" />;
  if (datasetList.length === 0) return (
    <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
      <div>{t("No dataset with task {task} yet. Create one and upload or import data first.", { task })}</div>
      <button className="btn-ghost mt-2 h-8" onClick={() => go("datasets")}>{t("Open Datasets")}</button>
    </div>);
  return (
    <div className="space-y-4">
      <Field label={t("Dataset")}>
        <select className="input" value={datasetId} onChange={(e) => onDataset(e.target.value)}>
          <option value="">{t("— pick a dataset —")}</option>
          {datasetList.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
        </select>
      </Field>
      <Field label={t("Version")}>
        <select className="input" value={versionId} onChange={(e) => onVersion(e.target.value)} disabled={!datasetId || versionList.length === 0}>
          {!version && <option value="">—</option>}
          {versionList.map((v, i) => <option key={v.id} value={v.id}>v{v.number}{i === 0 ? ` · ${t("latest")}` : ""}{hasSplit(v.splitState) ? "" : ` · ${t("not split")}`}</option>)}
        </select>
      </Field>
      {datasetId && versions.data && versionList.length === 0 && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
          <div>{t("This dataset has no version yet. Training reads an immutable version — create one on the dataset page.")}</div>
          <button className="btn-ghost mt-2 h-8" onClick={() => { writeSticky("dataset:tab", "versions"); go(`datasets/${datasetId}`); }}>{t("Open dataset")}</button>
        </div>)}
      {version && (
        <>
          <div className="text-xs text-fg-muted">{t("{n} files", { n: Number(version.fileCount).toLocaleString() })} · {bytes(version.sizeBytes)}{version.note ? ` · ${version.note}` : ""}</div>
          {hasSplit(version.splitState) ? (
            <div>
              <div className="mb-1.5 text-xs font-medium text-fg-muted">{version.splitState === "file" ? t("Fixed split saved with the version") : t("Split by the dataset's own folders")}</div>
              <SplitBar counts={version.splitCounts} />
            </div>
          ) : <SplitForm key={version.id} ws={ws} versionId={version.id} onDone={(v) => { void qc.invalidateQueries({ queryKey: ["versions", ws, datasetId] }); onVersion(v.id); }} />}
        </>)}
    </div>
  );
}

// ---- Trial (slice 3): a few-minute run of the draft through the training code path; Train opens after it passed. ----
const STAGES = ["data", "weights", "stage1", "resume", "package", "reload"] as const;
const STAGE_LABELS: Record<string, string> = {
  data: "Data", weights: "Load weights", stage1: "Stage 1", resume: "Resume from the checkpoint (stage 2)", package: "Package", reload: "Reload and predict" };
/** Which canvas node a stage belongs to (spec §8.4). */
const STAGE_NODE: Record<string, number> = { data: 2, weights: 3, stage1: 4, resume: 4, package: 5, reload: 5 };
const activeTrial = (x?: RecipeTrial) => x?.state === "queued" || x?.state === "running";
const stageLabel = (k: string) => t(STAGE_LABELS[k] ?? k);

/** "45 s", "12 min", "1 h 20 min". */
function dur(sec: number): string {
  const s = Math.max(0, Math.round(sec));
  if (s < 90) return t("{n} s", { n: s });
  const m = Math.round(s / 60);
  if (m < 90) return t("{n} min", { n: m });
  return t("{h} h {m} min", { h: Math.floor(m / 60), m: m % 60 });
}
/** Worst first: a failed stage outranks a running one, which outranks a pass. */
const RANK: Record<string, number> = { failed: 4, running: 3, pending: 2, passed: 1, skipped: 0 };
function worst(stages: TrialStage[]): TrialStage | undefined {
  return stages.reduce<TrialStage | undefined>((w, s) => (!w || (RANK[s.state] ?? 0) > (RANK[w.state] ?? 0) ? s : w), undefined);
}

function StageMark({ state }: { state: string }) {
  if (state === "passed") return <CircleCheck className="h-3.5 w-3.5 shrink-0 text-success" aria-label={t("passed")} />;
  if (state === "failed") return <CircleX className="h-3.5 w-3.5 shrink-0 text-danger" aria-label={t("failed")} />;
  if (state === "running") return <PlayCircle className="h-3.5 w-3.5 shrink-0 text-primary" aria-label={t("running")} />;
  if (state === "skipped") return <Minus className="h-3.5 w-3.5 shrink-0 text-fg-subtle" aria-label={t("skipped")} />;
  return <Circle className="h-3.5 w-3.5 shrink-0 text-fg-subtle" aria-label={t("pending")} />;
}

/** The trial's verdict on one node: the worst state of the stages that belong to it, and one short line. */
function NodeTrial({ trial, node }: { trial?: RecipeTrial; node: number }) {
  const mine = (trial?.stages ?? []).filter((s) => STAGE_NODE[s.key] === node && s.state !== "pending");
  const w = worst(mine);
  if (!w) return null;
  const secs = mine.reduce((a, s) => a + s.seconds, 0);
  const tone = w.state === "failed" ? "text-danger" : w.state === "passed" ? "text-success" : "text-fg-muted";
  return (
    <div className={`flex min-w-0 items-center gap-1.5 border-t border-line pt-1.5 text-[11px] ${tone}`} title={mine.map((s) => `${stageLabel(s.key)}: ${s.detail ? t(s.detail) : t(s.state)}`).join("\n")}>
      <StageMark state={w.state} />
      <span className="shrink-0">{t("trial")}{w.state === "passed" && secs > 0 ? ` · ${dur(secs)}` : ""}</span>
      {w.detail && <span className="min-w-0 truncate text-fg-muted">· {t(w.detail)}</span>}
    </div>
  );
}

function trialLine(x: RecipeTrial): { tone: string; text: string } {
  const total = x.stages.reduce((a, s) => a + s.seconds, 0);
  const running = x.stages.find((s) => s.state === "running");
  const failed = x.stages.find((s) => s.state === "failed");
  if (x.state === "queued") return { tone: "text-fg-muted", text: x.stateReason ? t("trial queued — {reason}", { reason: t(x.stateReason) }) : t("trial queued") };
  if (x.state === "running") return { tone: "text-primary", text: [x.hostName ? t("trial running on {host}", { host: x.hostName }) : t("trial running"), running ? stageLabel(running.key) : ""].filter(Boolean).join(" · ") };
  if (x.state === "passed") return { tone: "text-success", text: t("trial passed in {d}", { d: dur(total) }) };
  if (x.state === "failed") return { tone: "text-danger", text: failed ? t("trial failed at {stage}: {error}", { stage: stageLabel(failed.key), error: t(x.error || failed.detail) }) : t("trial failed: {error}", { error: t(x.error) }) };
  return { tone: "text-fg-muted", text: t("trial cancelled") };
}

function TrialCard({ trial, busy, onAgain, onLog }: { trial: RecipeTrial; busy: boolean; onAgain: () => void; onLog: () => void }) {
  const failed = trial.state === "failed";
  const byKey = Object.fromEntries(trial.stages.map((s) => [s.key, s]));
  const keys = [...STAGES, ...trial.stages.map((s) => s.key).filter((k) => !(STAGES as readonly string[]).includes(k))];
  const line = trialLine(trial);
  const gb = Number(trial.peakVramMb) / 1024;
  const measures = [
    trial.stepsPerSecond > 0 ? t("{n} steps/s", { n: trial.stepsPerSecond.toFixed(2) }) : "",
    gb > 0 ? `${t("GPU memory peak {n} GB", { n: gb.toFixed(1) })}${trial.vramEstimated ? ` (${t("estimated")})` : ""}` : "",
    trial.trainImages || trial.valImages ? t("{train} train / {val} val images", { train: trial.trainImages, val: trial.valImages }) : "",
  ].filter(Boolean);
  return (
    <div className={`card mt-3 p-3 ${failed ? "border-danger bg-danger/5" : ""}`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="flex min-w-0 items-center gap-2 text-sm font-semibold"><FlaskConical className="h-4 w-4 shrink-0 text-fg-subtle" />{t("Trial")}
          <span className={`truncate text-xs font-normal ${line.tone}`}>{line.text}</span></span>
        <span className="flex gap-2">
          {trial.runId && <button className="btn-ghost h-8" onClick={onLog}><FileText className="h-4 w-4" />{t("Open log")}</button>}
          {failed && <button className="btn-ghost h-8" disabled={busy} onClick={onAgain}><RotateCcw className="h-4 w-4" />{t("Run the trial again")}</button>}
        </span>
      </div>
      <ul className="mt-2 divide-y divide-line border-t border-line text-xs">
        {keys.map((k) => {
          const s = byKey[k], state = s?.state ?? "pending";
          return (
            <li key={k} className="py-1.5">
              <div className="flex items-center gap-2">
                <StageMark state={state} />
                <span className={`min-w-0 flex-1 ${state === "failed" ? "font-medium text-danger" : ""}`}>{stageLabel(k)}</span>
                <span className="shrink-0 tabular-nums text-fg-muted">{s && s.seconds > 0 ? dur(s.seconds) : state === "pending" ? "" : t(state)}</span>
              </div>
              {s?.detail && <div className={`ml-5 mt-0.5 ${state === "failed" ? "whitespace-pre-wrap break-words text-danger" : "truncate text-fg-muted"}`} title={t(s.detail)}>{t(s.detail)}</div>}
            </li>);
        })}
      </ul>
      {measures.length > 0 && <div className="mt-2 border-t border-line pt-2 text-xs text-fg-muted">{measures.join(" · ")}</div>}
    </div>
  );
}

/** First visit: the project takes its own copy of a Catalog recipe. */
function RecipePicker({ ws, projectId, task, onAdopted }: { ws: string; projectId: string; task: string; onAdopted: (pr: ProjectRecipe) => void }) {
  const recipes = useQuery({ queryKey: ["recipes", ws, task], queryFn: () => api.catalog.listRecipes({ workspaceId: ws, task }) });
  const trainable = useMemo(() => (recipes.data?.recipes ?? []).filter((r) => r.entrypoints.includes("train")), [recipes.data]);
  // The list carries no manifest, and the `canvas` key lives there.
  const canvas = useQuery({
    queryKey: ["recipes-canvas", ws, trainable.map((r) => r.name).join()], enabled: trainable.length > 0,
    queryFn: async () => (await Promise.all(trainable.map((r) => api.catalog.getRecipe({ workspaceId: ws, name: r.name }).catch(() => null))))
      .filter((r) => r && parseJson<Manifest>(r.manifestJson, {}).canvas !== undefined).map((r) => r!.name),
  });
  const usable = (name: string) => name === "d-fine" || (canvas.data ?? []).includes(name);
  const adopt = useMutation({ mutationFn: (recipe: string) => api.tracking.adoptRecipe({ workspaceId: ws, projectId, recipe }), onSuccess: onAdopted });
  const sorted = useMemo(() => [...trainable].sort((a, b) => Number(usable(b.name)) - Number(usable(a.name))), [trainable, canvas.data]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <div className="card">
      <div className="text-sm font-semibold">{t("Recipe")}</div>
      <div className="mt-4"><ErrorBox error={recipes.error ? errorText(recipes.error) : adopt.error ? errorText(adopt.error) : null} /></div>
      {recipes.isLoading ? <div className="skeleton h-20" /> : trainable.length === 0 ? (
        <div className="rounded-lg border border-line p-3 text-sm text-fg-muted">{t("No recipe in the catalog can train task {task}.", { task })}</div>
      ) : (
        <div className="grid gap-2 md:grid-cols-2 xl:grid-cols-3">
          {sorted.map((r) => {
            const bad = r.state === "invalid", ok = usable(r.name) && !bad;
            return (
              <button key={r.id} type="button" disabled={!ok || adopt.isPending} aria-label={r.displayName} onClick={() => adopt.mutate(r.name)}
                className="rounded-lg border border-line p-3 text-left transition enabled:hover:border-primary disabled:cursor-not-allowed disabled:opacity-50">
                <div className="flex items-start justify-between gap-2">
                  <span className="text-sm font-medium">{r.displayName}</span>
                  {r.requiresAck && <span className="badge shrink-0 bg-amber-100 text-amber-700"><TriangleAlert className="h-3 w-3" />{t("license")}</span>}
                </div>
                <div className="mt-1 text-xs text-fg-muted">{t("{n} variants", { n: r.variants.length })} · {r.licenseCode}{r.gpu === "required" ? ` · ${t("needs GPU")}` : ""}</div>
                {!ok && <div className={`mt-1 text-xs ${bad ? "text-danger" : "text-fg-subtle"}`}>
                  {bad ? t("The recipe manifest is invalid") : t("not available on the canvas yet")}
                </div>}
              </button>);
          })}
        </div>)}
    </div>
  );
}

/** S-09-4 Recipe tab: what the machine will do when it trains, drawn as six parts; every part opens its details on the right. */
export function RecipeCanvas({ ws, projectId, task, runs, go, onStarted }: {
  ws: string; projectId: string; task: string; runs: Run[]; go: (path: string) => void; onStarted: (run: Run) => void;
}) {
  const qc = useQueryClient();
  const recipes = useQuery({ queryKey: ["project-recipes", ws, projectId], queryFn: () => api.tracking.listProjectRecipes({ workspaceId: ws, projectId }) });
  const list = recipes.data?.recipes ?? [];
  const [picked, setPicked] = useSticky(stickyKey(projectId), "");
  const current = list.find((r) => r.id === picked) ?? list.find((r) => r.recipe === runs[0]?.recipe) ?? list[0];
  if (recipes.isLoading) return <div className="skeleton h-64" />;
  if (recipes.error) return <ErrorBox error={errorText(recipes.error)} onRetry={() => void recipes.refetch()} />;
  if (!current) return <RecipePicker ws={ws} projectId={projectId} task={task}
    onAdopted={(pr) => { setPicked(pr.id); void qc.invalidateQueries({ queryKey: ["project-recipes", ws, projectId] }); }} />;
  return <Canvas key={current.id} ws={ws} projectId={projectId} task={task} runs={runs} go={go} onStarted={onStarted} recipes={list} pr={current} onPick={setPicked} />;
}

function Canvas({ ws, projectId, task, runs, go, onStarted, recipes, pr, onPick }: {
  ws: string; projectId: string; task: string; runs: Run[]; go: (path: string) => void; onStarted: (run: Run) => void;
  recipes: ProjectRecipe[]; pr: ProjectRecipe; onPick: (id: string) => void;
}) {
  const qc = useQueryClient();
  const d = useDraft(ws, pr.id);
  const recipe = d.state?.recipe ?? pr;
  const manifest = useMemo(() => parseJson<Manifest>(recipe.manifestJson, {}), [recipe.manifestJson]);
  const defs = useMemo(() => Object.entries(manifest.params ?? {}).filter(([, x]) => !x.from), [manifest]);
  const variants = useMemo(() => (manifest.variants ?? []).filter((v) => !v.tasks?.length || v.tasks.includes(task)), [manifest, task]);
  const catalog = useQuery({ queryKey: ["recipe", ws, recipe.recipe], queryFn: () => api.catalog.getRecipe({ workspaceId: ws, name: recipe.recipe }) });
  const map = useQuery({ queryKey: ["cluster-map", ws], queryFn: () => api.ops.getClusterMap({ workspaceId: ws }), refetchInterval: 10_000 });

  const [sel, setSel] = useState<Sel | null>(null);
  const [raw, setRaw] = useState<Raw>({});
  const [ack, setAck] = useState(false);
  const [showChanges, setShowChanges] = useState(false);
  const [trialOpen, setTrialOpen] = useState(false);
  const [level, setLevel] = useState<Level>("visual");
  const [datasetPick, setDatasetPick] = useState<{ epoch: number; id: string } | null>(null);
  useEffect(() => { setRaw(toRaw(d.draft?.paramsJson)); }, [d.epoch]); // eslint-disable-line react-hooks/exhaustive-deps
  // H1: "Train with this version" from Labeling opens the canvas with ?version=<id> — applied once to the draft, then dropped from the URL.
  useEffect(() => {
    const u = new URL(window.location.href), v = u.searchParams.get("version");
    if (!v || !d.draft) return;
    u.searchParams.delete("version"); window.history.replaceState(window.history.state, "", u);
    if (d.draft.datasetVersionId !== v) d.edit({ datasetVersionId: v });
  }, [d.epoch]); // eslint-disable-line react-hooks/exhaustive-deps

  const draft = d.draft;
  const changes = d.state?.changes ?? [];
  const base = draft?.baseRevision ?? 0;
  const gpuRequired = manifest.environment?.gpu === "required";
  const requiresAck = catalog.data?.requiresAck ?? false;
  const hosts = map.data?.hosts ?? [];
  const online = hosts.filter((h) => h.state === "online" || h.state === "busy");
  const host = draft?.hostId ? hosts.find((h) => h.id === draft.hostId) : undefined;
  const pool = draft?.hostId ? online.filter((h) => h.id === draft.hostId) : online;
  const allGpus = pool.flatMap((h) => h.gpus.map((g) => ({ ...g, host: h.name })));
  // D-50: a GPU is shared by memory budget — it has room while the budgets of the jobs on it stay below its memory.
  const freeGpus = allGpus.filter((g) => Number(g.reservedBytes) < Number(g.memoryBytes));
  const gpuFree = allGpus.reduce((s, g) => s + Math.max(0, Number(g.memoryBytes) - Number(g.reservedBytes)), 0);
  const gpuTotal = allGpus.reduce((s, g) => s + Number(g.memoryBytes), 0);
  const gpuJobs = [...new Set(allGpus.flatMap((g) => (g.jobName ? g.jobName.split(", ") : [])))].join(", ");
  const maxCpus = Math.max(0, ...pool.map((h) => h.cpus));
  // A draft the server left without resources gets what the old form started with.
  useEffect(() => {
    if (!draft || !map.data) return;
    const patch: Partial<Draft> = {};
    if (gpuRequired && draft.gpus < 1) patch.gpus = 1;
    if (draft.cpus <= 0) patch.cpus = maxCpus > 0 ? Math.max(1, Math.min(4, Math.floor(maxCpus / 2))) : 2;
    if (Object.keys(patch).length > 0) d.edit(patch);
  }, [d.epoch, !map.data]); // eslint-disable-line react-hooks/exhaustive-deps

  const paramErrors = useMemo(() => Object.fromEntries(defs.map(([k, x]) => [k, checkParam(x, raw[k])]).filter(([, e]) => e)) as Record<string, string>, [defs, raw]);
  const train = useMutation({
    mutationFn: async () => ((await d.flush()) && d.gateOpen() ? api.tracking.startTraining({ workspaceId: ws, projectId, projectRecipeId: pr.id }) : null),
    onSuccess: (run) => { if (run) { void qc.invalidateQueries({ queryKey: ["runs", ws, projectId] }); onStarted(run); } },
  });
  const reset = useMutation({
    mutationFn: (req: { revisionNumber?: number; origin?: boolean }) => d.reset(req),
    onSuccess: () => { setShowChanges(false); void qc.invalidateQueries({ queryKey: ["project-recipes", ws, projectId] }); },
  });
  // The trial shown is the gate's (it belongs to the draft as it is now); polled copies replace it while it runs.
  const gateTrial = d.state?.gate?.trial;
  const [live, setLive] = useState<RecipeTrial | null>(null);
  const trial = gateTrial && live?.id === gateTrial.id ? live : gateTrial;
  const refreshGate = async () => { if (await d.flush()) await d.reload(); };
  const startTrial = useMutation({
    mutationFn: async () => ((await d.flush()) ? api.tracking.startTrial({ workspaceId: ws, projectRecipeId: pr.id }) : null),
    onSuccess: (x) => { if (x) { setLive(x); void d.reload(); } },
  });
  const cancelTrial = useMutation({
    mutationFn: (id: string) => api.tracking.cancelTrial({ workspaceId: ws, trialId: id }),
    onSuccess: (x) => { setLive(x); void refreshGate(); },
  });
  const trialId = trial?.id ?? "", trialActive = activeTrial(trial);
  useEffect(() => {
    if (!trialId || !trialActive) return;
    let gone = false;
    const timer = window.setInterval(() => {
      api.tracking.getTrial({ workspaceId: ws, trialId }).then((x) => {
        if (gone) return;
        setLive(x);
        if (!activeTrial(x)) void refreshGate();   // the gate (canTrain, estimate) changes when a trial ends
      }, () => {});
    }, 3000);
    return () => { gone = true; window.clearInterval(timer); };
  }, [ws, trialId, trialActive]); // eslint-disable-line react-hooks/exhaustive-deps
  const envs = useEnvironments(ws, recipe.recipe);
  const envList = envs.data?.environments ?? [];
  const env = envFor(envList, draft?.hostId ?? "");
  // What an inspection depends on; a recipe that flags no parameter is inspected again on every parameter change.
  const inspectKey = useMemo(() => {
    const flagged = defs.filter(([, x]) => x.affectsInspect).map(([k]) => k), keys = new Set(flagged.length > 0 ? flagged : defs.map(([k]) => k));
    return JSON.stringify(Object.entries(parseJson<Record<string, unknown>>(draft?.paramsJson, {})).filter(([k]) => keys.has(k)).sort(([a], [b]) => (a < b ? -1 : 1)));
  }, [defs, draft?.paramsJson]);
  const insp = useInspection({ ws, recipe: recipe.recipe, variant: draft?.variant ?? "", paramsJson: draft?.paramsJson || "{}", inspectKey, hostId: draft?.hostId ?? "", envReady: env?.state === "ready" && env.online });
  const view = insp.view;
  const f = useMemo(() => (view ? facts(view.cfg) : null), [view]);
  const revisions = useQuery({ queryKey: ["revisions", ws, pr.id, base], queryFn: () => api.tracking.listRevisions({ workspaceId: ws, projectRecipeId: pr.id }), enabled: base > 0 && changes.length > 0 });
  const baseRev = revisions.data?.revisions.find((r) => r.number === base);
  const baseCfg = useBaseInspection({ ws, recipe: recipe.recipe, hostId: draft?.hostId ?? "", enabled: changes.length > 0,
    variant: base === 0 ? variants[0]?.name ?? "" : baseRev?.variant ?? "", paramsJson: base === 0 ? "{}" : baseRev?.paramsJson || "{}" });
  const diff = useMemo(() => (baseCfg && view ? configDiff(baseCfg, view.cfg) : null), [baseCfg, view]);
  if (!draft || !d.state) return d.error ? <ErrorBox error={d.error} onRetry={() => void d.reload()} /> : <div className="skeleton h-64" />;

  // R5: while training again, the recipe of #n is locked (server enforces it too); only data, machine and name change.
  const locked = draft.trainKind === "retrain";
  const retrainOf = locked ? runs.find((r) => r.id === draft.retrainOfRunId) : undefined;
  const pickVariant = (name: string) => { if (!locked) d.edit({ variant: name }); };
  const setParams = (patch: Record<string, string | boolean | undefined>) => {
    if (locked) return;
    const next = { ...raw };
    for (const [k, v] of Object.entries(patch)) if (v === undefined) delete next[k]; else next[k] = v;
    setRaw(next); d.edit({ paramsJson: paramsJson(defs, next, draft.paramsJson) });
  };
  const setParam = (k: string, v: string | boolean | undefined) => setParams({ [k]: v });
  const changeOf = (k: string) => changes.find((c) => c.field === `params.${k}`);
  const paneParams = (p: PaneKey) => defs.filter(([k, x]) => paneOf(k, x) === p);
  const edited = (key: string, fields: string[] = []) => changes.some((c) => c.pane === key || fields.includes(c.field));
  const epochsDef = defs.find(([k]) => k === "epochs")?.[1];
  const epochs = epochsDef ? shown(epochsDef, raw.epochs) : undefined;
  const ds = d.state.dataset;
  const datasetId = datasetPick?.epoch === d.epoch ? datasetPick.id : ds?.datasetId ?? "";
  const kindText = `${locked ? t("Train again from #{n}", { n: retrainOf?.number ?? "?" }) : t("Fine-tune")}${draft.startKind === "backbone" ? ` · ${t("backbone only")}` : ""}`;
  const autoName = draft.variant ? `${draft.variant}-${Math.max(0, ...runs.map((r) => r.number)) + 1}` : "";
  const best = manifest.artifacts?.best;

  // Drawn from the inspection; nothing of this exists before the first one came back.
  const total = f?.epochs ?? 0;
  const spans = view && total > 0 ? phaseSpans(manifest.canvas?.phases ?? [], view.cfg, total) : [];
  const batchDef = defs.find(([k]) => k === "batch")?.[1];
  const batchSize = Number((raw.batch === undefined || raw.batch === "" || paramErrors.batch ? batchDef?.default : raw.batch) ?? 0) || f?.batch || 0;
  const trainCount = Number(ds?.splitCounts.train ?? 0);
  const perEpoch = trainCount > 0 && batchSize > 0 ? Math.max(1, f?.dropLast ? Math.floor(trainCount / batchSize) : Math.ceil(trainCount / batchSize)) : undefined;
  const slower = f?.lr && f.backboneLr && f.backboneLr < f.lr ? f.lr / f.backboneLr : undefined;
  const blockLabels = Object.fromEntries((manifest.canvas?.blocks ?? []).map((b) => [b.key, b.label]));
  // The config shows the variant's own value only for a parameter that inspection did not set.
  const own = (k: string) => !!view && view.variant === draft.variant && !(k in view.params);
  const real: Record<string, string | undefined> = f ? {
    lr: own("lr") && f.lr !== undefined ? fmtNum(f.lr) : undefined,
    img_size: own("img_size") && f.imgSize ? String(f.imgSize[0]) : undefined,
    backbone_lr_scale: own("backbone_lr_scale") && f.lr && f.backboneLr !== undefined ? fmtNum(f.backboneLr / f.lr) : undefined } : {};
  const unset = (v: string | boolean | undefined) => v === undefined || v === "";
  const preset = raw.freeze === "backbone" ? 0 : unset(raw.freeze) && unset(raw.backbone_lr_scale) ? 1 : !unset(raw.backbone_lr_scale) && Number(raw.backbone_lr_scale) === 1 ? 2 : -1;
  const pickPreset = (i: number) => setParams(i === 0 ? { freeze: "backbone" } : { freeze: undefined, backbone_lr_scale: i === 2 ? "1" : undefined });
  const summary = (p: PaneKey, compact: boolean): ReactNode => {
    if (!view || !f) return null;
    const small = compact ? "text-[11px] text-fg-muted" : "text-sm text-fg-muted";
    if (p === "schedule") return total > 0 ? <ScheduleStrip spans={spans} total={total} warmup={f.warmup} perEpoch={perEpoch} /> : null;
    if (p === "learn") return <Blocks blocks={view.insp.blocks} labels={blockLabels} slower={slower} compact={compact} />;
    if (p === "objective") return compact ? <div className={small}>{[f.optimizer, `EMA ${f.ema ? t("on") : t("off")}`].filter(Boolean).join(" · ")}</div> : <ObjectiveTable f={f} />;
    if (p === "batch") return compact ? <div className={small}>{batchLine(f)}</div> : <AugList f={f} />;
    if (p === "checkpoint") return checkpointText(f) ? <div className={small}>{checkpointText(f)}</div> : null;
    return null;
  };
  const onPane = PANES.some((p) => p.key === sel), wide = onPane && level !== "visual";

  const gpuNote: { tone: "ok" | "warn"; text: string } | null = !map.data ? null
    : draft.hostId && pool.length === 0 ? { tone: "warn", text: t("The chosen machine is offline — the run waits in the queue until it connects.") }
    : online.length === 0 ? { tone: "warn", text: t("No machine is online — the run waits in the queue until one connects.") }
    : draft.gpus === 0 ? null
    : allGpus.length < draft.gpus ? { tone: "warn", text: t("Online machines have {n} GPU in total — this run would wait forever.", { n: allGpus.length }) }
    : freeGpus.length >= draft.gpus ? { tone: "ok", text: t("GPU memory free: {free} of {total}", { free: bytes(gpuFree), total: bytes(gpuTotal) }) + (gpuJobs ? ` · ${gpuJobs}` : "") }
    : { tone: "warn", text: t("GPU memory fully reserved by: {jobs}", { jobs: gpuJobs }) };

  const mine = [
    ...(Object.keys(paramErrors).length > 0 ? [t("Fix the highlighted parameters")] : []),
    ...(gpuRequired && draft.gpus < 1 ? [t("This recipe needs a GPU")] : []),
    ...(requiresAck && !ack ? [t("Accept the license terms")] : [])];
  const reasons = [...(d.state.gate?.reasons ?? []).map((x) => t(x)), ...mine];
  const blocked = !d.state.gate?.canTrain || mine.length > 0;
  const estimate = d.state.gate?.estimate;

  const title = sel ? t(PANES.find((p) => p.key === sel)?.label ?? TITLES[sel as keyof typeof TITLES]) : "";
  const body = (): ReactNode => {
    if (sel === "machine") return (
      <div className="space-y-4">
        <Field label={t("Machine")}>
          <select className="input" value={draft.hostId} onChange={(e) => d.edit({ hostId: e.target.value })}>
            <option value="">{t("Any machine that fits")}</option>
            {host && !online.includes(host) && <option value={host.id}>{host.name} · {t("offline")}</option>}
            {draft.hostId && !host && <option value={draft.hostId}>{draft.hostId.slice(0, 8)}</option>}
            {online.map((h) => <option key={h.id} value={h.id}>{h.name} · {h.gpus.length} GPU · {h.cpus} CPU</option>)}
          </select>
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="GPU">
            <input className="input" type="number" min={gpuRequired ? 1 : 0} max={8} value={draft.gpus} onChange={(e) => d.edit({ gpus: Math.max(0, Math.min(8, Math.round(Number(e.target.value)))) })} />
          </Field>
          <Field label="CPU">
            <input className="input" type="number" min={1} max={maxCpus || 64} value={draft.cpus} onChange={(e) => d.edit({ cpus: Math.max(1, Math.min(maxCpus || 64, Math.round(Number(e.target.value)))) })} />
          </Field>
        </div>
        {gpuNote && (
          <div className={`flex items-start gap-2 text-xs ${gpuNote.tone === "ok" ? "text-fg-muted" : "text-warning"}`}>
            {gpuNote.tone === "ok" ? <Cpu className="mt-0.5 h-3.5 w-3.5 shrink-0" /> : <CircleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" />}<span>{gpuNote.text}</span>
          </div>)}
        <div className="border-t border-line pt-4"><EnvPanel ws={ws} recipe={recipe.recipe} envs={envList} error={envs.error ? errorText(envs.error) : undefined} /></div>
        <div className="border-t border-line pt-4">
          <Field label={t("Run name")}>
            <input className="input" value={draft.runName} maxLength={80} placeholder={autoName} onChange={(e) => d.edit({ runName: e.target.value })} />
          </Field>
        </div>
      </div>);
    if (sel === "data") return <DataPanel ws={ws} task={task} go={go} datasetId={datasetId} versionId={draft.datasetVersionId}
      onDataset={(id) => { setDatasetPick({ epoch: d.epoch, id }); if (id !== datasetId) d.edit({ datasetVersionId: "" }); }} onVersion={(id) => d.edit({ datasetVersionId: id })} />;
    if (sel === "model") return (
      <div className="space-y-4">
        <div className="overflow-hidden rounded-lg border border-line">
          <table className="w-full text-sm">
            <thead className="border-b border-line text-left text-xs text-fg-muted"><tr>
              <th className="px-3 py-1.5 font-medium">{t("Variant")}</th><th className="px-3 py-1.5 text-right font-medium">{t("Parameters")}</th><th className="px-3 py-1.5 font-medium">{t("Reported score")}</th></tr></thead>
            <tbody>{variants.map((v) => {
              const on = v.name === draft.variant;
              return (
                <tr key={v.name} className={`cursor-pointer border-b border-line last:border-0 ${on ? "bg-primary/10" : "hover:bg-surface-2"}`} onClick={() => pickVariant(v.name)}>
                  <td className="px-3 py-1.5"><label className="flex cursor-pointer items-center gap-2"><input type="radio" name="variant" checked={on} disabled={locked && !on} onChange={() => pickVariant(v.name)} /><span className="font-medium">{v.displayName || v.name}</span></label></td>
                  <td className="whitespace-nowrap px-3 py-1.5 text-right tabular-nums text-fg-muted">{v.params ? `${v.params}M` : "—"}</td>
                  <td className="px-3 py-1.5 text-xs text-fg-muted">{Object.entries(v.reported ?? {}).map(([k, x]) => `${k} ${x}`).join(" · ") || "—"}</td>
                </tr>);
            })}</tbody>
          </table>
          {variants.length === 0 && <div className="p-3 text-sm text-fg-muted">{t("This recipe lists no variant for task {task}.", { task })}</div>}
        </div>
        <div className="grid grid-cols-2 gap-2">
          <button type="button" aria-pressed={draft.trainKind !== "retrain"} onClick={() => d.edit({ trainKind: "fine_tune", retrainOfRunId: "" })}
            className={`rounded-lg border p-3 text-left ${draft.trainKind !== "retrain" ? "border-primary bg-primary/10" : "border-line hover:border-fg-subtle"}`}>
            <div className="text-sm font-medium">{t("Fine-tune")}</div>
          </button>
          <div aria-pressed={locked} className={`rounded-lg border p-3 ${locked ? "border-primary bg-primary/10" : "border-line opacity-60"}`}>
            <div className="text-sm font-medium">{t("Train again (re-training)")}{retrainOf ? ` #${retrainOf.number}` : ""}</div>
          </div>
        </div>
        {manifest.entrypoints?.train?.fromScratch === true && (
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" className="mt-1" disabled={locked} checked={draft.startKind === "backbone"} onChange={(e) => d.edit({ startKind: e.target.checked ? "backbone" : "pretrained" })} />
            <span>{t("Keep only the pretrained backbone")}</span>
          </label>)}
        {requiresAck && catalog.data && (
          <label className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            <input type="checkbox" className="mt-1" checked={ack} onChange={(e) => setAck(e.target.checked)} />
            <span><b>{t("License {code}.", { code: catalog.data.licenseCode })}</b> {catalog.data.licenseNotes} — {t("I understand the terms of use.")}</span>
          </label>)}
      </div>);
    if (sel === "package") return (
      <div className="space-y-3 text-sm">
        <ul className="divide-y divide-line rounded-lg border border-line">
          {best?.as && <li className="flex items-center justify-between gap-2 px-3 py-2"><span className="font-mono text-xs">{best.as}</span><span className="badge bg-surface-2 text-fg-muted">{t("best checkpoint")}</span></li>}
          {(manifest.artifacts?.files ?? []).map((f) => <li key={f.from} className="px-3 py-2 font-mono text-xs">{f.to ?? f.from}</li>)}
          {!best?.as && (manifest.artifacts?.files ?? []).length === 0 && <li className="px-3 py-2 text-fg-muted">{t("The recipe does not list its files.")}</li>}
        </ul>
        <button className="btn-ghost" onClick={() => go("models")}>{t("Open Models")}</button>
      </div>);
    if (sel === "evaluate") return (
      <div className="space-y-3 text-sm">
        <button className="btn-ghost" onClick={() => go("models")}>{t("Open Models")}</button>
      </div>);
    if (!sel) return null;
    const list = paneParams(sel), top = summary(sel, false), code = paneSources(view?.insp, sel);
    return (
      <>
        <LevelSwitch value={code.length > 0 ? level : level === "code" ? "visual" : level} onChange={setLevel} code={code.length > 0} />
        {level === "config" ? <ConfigView view={view} outdated={insp.outdated} /> : level === "code" && code.length > 0 ? <CodeView key={sel} sources={code} outdated={insp.outdated} /> : (
          <div className="space-y-4">
            {top && insp.outdated && <div className="flex justify-end"><Outdated /></div>}
            {top}
            {sel === "schedule" && f && total > 0 && <LrChart f={f} total={total} perEpoch={perEpoch} spans={spans} />}
            {sel === "learn" && defs.some(([k]) => k === "freeze") && <Presets active={preset} onPick={pickPreset} />}
            {list.map(([k, x]) => <ParamField key={k} name={k} def={x} value={raw[k]} error={paramErrors[k]} change={changeOf(k)} placeholder={real[k]} onChange={(v) => setParam(k, v)} />)}
          </div>)}
      </>);
  };
  return (
    <>
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1.5">
          {recipes.length > 1 ? (
            <select className="input h-9 w-auto font-medium" aria-label={t("Recipe")} value={pr.id} onChange={(e) => onPick(e.target.value)}>
              {recipes.map((r) => <option key={r.id} value={r.id}>{r.displayName || r.recipe}</option>)}
            </select>
          ) : <span className="text-base font-semibold">{recipe.displayName || recipe.recipe}</span>}
          <span className="text-sm text-fg-muted">{t("your draft")} · {base === 0 ? t("from the recipe's defaults") : changes.length === 0 ? t("same as r{rev}", { rev: base }) : t("{n} changes since r{rev}", { n: changes.length, rev: base })}</span>
          {d.status && <span className="text-xs text-fg-subtle">{d.status === "saving" ? t("Saving…") : t("Saved")}</span>}
          {recipe.originChanged && (
            <span className="inline-flex flex-wrap items-center gap-2">
              <span className="badge bg-amber-100 text-amber-700"><TriangleAlert className="h-3 w-3" />{t("recipe changed in the Catalog")}</span>
              <button className="btn-ghost h-7 px-2 text-xs" disabled={reset.isPending} onClick={() => reset.mutate({ origin: true })}>{t("Take the Catalog version")}</button>
            </span>)}
        </div>
        <div className="flex flex-col items-end gap-1">
          <div className="flex items-center gap-2">
            {trialActive && trial ? (
              <button className="btn-ghost" disabled={cancelTrial.isPending} onClick={() => cancelTrial.mutate(trial.id)}><Square className="h-4 w-4" />{t("Cancel trial")}</button>
            ) : (
              <button className="btn-ghost" disabled={!d.state.gate?.canTrial || mine.length > 0 || startTrial.isPending} onClick={() => startTrial.mutate()}>
                <FlaskConical className="h-4 w-4" />{startTrial.isPending ? t("Starting…") : t("Run a trial")}</button>)}
            <button className="btn-primary" disabled={blocked || train.isPending} onClick={() => train.mutate()}><Play className="h-4 w-4" />{train.isPending ? t("Starting…") : t("Train")}</button>
          </div>
          {estimate && <div className="max-w-xs text-right text-xs text-fg-muted">
            {t("about {low}–{high}", { low: dur(estimate.lowSeconds), high: dur(estimate.highSeconds) })}
</div>}
          {reasons.map((x) => <div key={x} className="max-w-xs text-right text-xs text-warning">{x}</div>)}
        </div>
      </div>
      {d.notice && <div className="mb-4 rounded-lg border border-line bg-surface-2 px-3 py-2 text-sm text-fg-muted">{d.notice}</div>}
      {locked && (
        <div className="mb-4 flex flex-wrap items-center gap-3 rounded-lg border border-primary/40 bg-primary/10 px-3 py-2 text-sm">
          <span><b>{t("Train again from #{n}", { n: retrainOf?.number ?? "?" })}</b></span>
          <button className="btn-ghost ml-auto h-8" onClick={() => d.edit({ trainKind: "fine_tune", retrainOfRunId: "" })}>{t("Switch to Fine-tune")}</button>
        </div>)}
      <ErrorBox error={d.error || ([train.error, startTrial.error, cancelTrial.error, reset.error].filter(Boolean).map((e) => errorText(e))[0] ?? null)} />

      <div className="flex items-start gap-4">
        <div className="@container min-w-0 flex-1">
          <div className="grid gap-3 @5xl:grid-cols-[minmax(0,3fr)_minmax(0,1fr)]">
            <div className="order-1 grid gap-3 @2xl:grid-cols-3">
              <Node n={1} title={TITLES.machine} icon={Cpu} active={sel === "machine"} edited={edited("machine")} onClick={() => setSel("machine")}>
                <div className="truncate text-fg">{draft.hostId ? host?.name ?? draft.hostId.slice(0, 8) : t("Any machine that fits")}</div>
                <div>{draft.gpus} GPU · {draft.cpus} CPU</div>
                {gpuNote && <div className={gpuNote.tone === "warn" ? "text-warning" : ""}>{gpuNote.text}</div>}
                <EnvLine env={env} />
              </Node>
              <Node n={2} title={TITLES.data} icon={Database} active={sel === "data"} edited={edited("data")} onClick={() => setSel("data")}>
                {ds ? (
                  <>
                    <div className="truncate text-fg">{ds.datasetName} <span className="text-fg-subtle">v{ds.versionNumber}</span></div>
                    <div>{t("{n} files", { n: Number(ds.fileCount).toLocaleString() })}</div>
                    {hasSplit(ds.splitState) ? <SplitBar counts={ds.splitCounts} />
                      : <div className="flex items-center gap-1 text-warning"><TriangleAlert className="h-3 w-3 shrink-0" />{t("no train/val split yet")}</div>}
                    <TransferEstimate ws={ws} versionId={draft.datasetVersionId} hostId={draft.hostId} />
                  </>
                ) : <div className="text-warning">{t("no dataset version chosen")}</div>}
                <NodeTrial trial={trial} node={2} />
              </Node>
              <Node n={3} title={TITLES.model} icon={Boxes} active={sel === "model"} edited={edited("model", ["variant"])} onClick={() => setSel("model")}>
                <div className={`truncate ${draft.variant ? "text-fg" : "text-warning"}`}>{draft.variant || t("no variant chosen")}</div>
                <div>{kindText}</div>
                <NodeTrial trial={trial} node={3} />
              </Node>
            </div>

            <div className="card order-2 flex flex-col p-3 @5xl:order-3 @5xl:col-span-2">
              <div className="flex items-center gap-2"><Num n={4} /><span className="text-sm font-semibold">{t("Training loop")}</span><Repeat className="h-3.5 w-3.5 text-fg-subtle" />
                {epochs !== undefined && <span className="ml-auto text-xs text-fg-muted">{t("{n} epochs", { n: epochs })}</span>}</div>
              {trial && <div className="mt-2 text-xs"><NodeTrial trial={trial} node={4} /></div>}
              {draft.variant && <InspectLine status={insp.status} error={insp.error} onOpenEnv={() => setSel("machine")} onRetry={insp.retry} />}
              <div className="@container mt-3">
                <ParamBoard defs={defs} raw={raw} errors={paramErrors} changed={(k) => !!changeOf(k)} paneOf={(k, x) => paneOf(k, x as ParamDef)} placeholders={real}
                  disabled={locked} onChange={setParam} onReset={(k) => {
                    const c = changeOf(k), x = defs.find(([n]) => n === k)?.[1];
                    if (!c || !x) return setParam(k, undefined);
                    setParam(k, c.from === "" || (!noDefault(x) && c.from === String(x.default)) ? undefined : kind(x) === "boolean" ? c.from === "true" : c.from);
                  }} onDetails={(g) => setSel(g as PaneKey)} />
              </div>
            </div>

            <div className="order-3 grid gap-3 @xl:grid-cols-2 @5xl:order-2 @5xl:grid-cols-1">
              <Node n={5} title={TITLES.package} icon={Package} active={sel === "package"} onClick={() => setSel("package")}>
                {best?.as ? <><div className="truncate font-mono text-fg">{best.as}</div><div>{t("best checkpoint")}</div></> : <div>—</div>}
                <NodeTrial trial={trial} node={5} />
              </Node>
              <Node n={6} title={TITLES.evaluate} icon={Gauge} active={sel === "evaluate"} onClick={() => setSel("evaluate")} />
            </div>
          </div>

          {trial && (
            <button type="button" onClick={() => setTrialOpen(true)}
              className={`mt-3 flex h-12 w-full items-center gap-3 rounded-xl border px-4 text-left text-sm active:scale-[0.99] ${trial.state === "failed" ? "border-danger bg-danger/5" : "border-line bg-surface-2 hover:border-primary/50"}`}>
              <FlaskConical className="h-4 w-4 shrink-0 text-fg-subtle" /><span className="font-medium">{t("Trial")}</span>
              <span className={`min-w-0 flex-1 truncate ${trialLine(trial).tone}`}>{trialLine(trial).text}</span><ChevronRight className="h-4 w-4 shrink-0 text-fg-subtle" />
            </button>)}
          {trial && trialOpen && (
            <Dialog title={t("Trial")} wide onClose={() => setTrialOpen(false)}>
              <TrialCard trial={trial} busy={startTrial.isPending || !d.state.gate?.canTrial} onAgain={() => startTrial.mutate()} onLog={() => go(`projects/${projectId}/runs/${trial.runId}`)} />
            </Dialog>)}
          {changes.length > 0 && (
            <div className="card mt-3 p-3">
              <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
                <span>{base > 0 ? t("{n} changes since r{rev}", { n: changes.length, rev: base }) : t("{n} changes from the recipe's defaults", { n: changes.length })}</span>
                <span className="flex gap-2">
                  <button className="btn-ghost h-8" onClick={() => setShowChanges(!showChanges)}>{showChanges ? t("Hide") : t("Show")}</button>
                  <button className="btn-ghost h-8" disabled={reset.isPending} onClick={() => reset.mutate(base > 0 ? { revisionNumber: base } : { origin: true })}><Undo2 className="h-4 w-4" />{t("Discard changes")}</button>
                </span>
              </div>
              <div className="mt-2 border-t border-line pt-2 text-xs">
                {!diff ? <span className="text-fg-subtle">{t("not inspected yet")}</span>
                  : diff.length === 0 ? <span className="text-fg-subtle">{t("config unchanged")}</span> : (
                    <>
                      <div className="mb-1 flex items-center gap-2 text-fg-muted">{t("{n} config keys change", { n: diff.length })}{insp.outdated && <Outdated />}</div>
                      <ul className="space-y-0.5 font-mono">{diff.slice(0, 12).map((x) => <li key={x.key} className="break-all"><span className="text-fg-muted">{x.key}:</span> {x.from} → <b>{x.to}</b></li>)}</ul>
                      {diff.length > 12 && <div className="mt-1 text-fg-subtle">{t("and {n} more", { n: diff.length - 12 })}</div>}
                    </>)}
              </div>
              {showChanges && (
                <ul className="mt-2 space-y-1 border-t border-line pt-2 text-xs">
                  {changes.map((c) => <li key={c.field} className="flex flex-wrap gap-x-2"><span className="font-mono text-fg-muted">{c.field.replace(/^params\./, "")}</span><span>{c.from || "—"} → <b>{c.to || "—"}</b></span></li>)}
                </ul>)}
            </div>)}
        </div>

      </div>
      {/* Detail always opens in the shared right sheet (no-popups rule): never as a column that squeezes the canvas. */}
      {sel && <Dialog title={title} wide={wide} onClose={() => setSel(null)}>{body()}</Dialog>}
    </>
  );
}
