import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, BrainCircuit, CheckCircle2, CircleDashed, CircleX, Lightbulb, Loader2, RotateCcw, ShieldCheck, Sparkles, Zap } from "lucide-react";
import { api, errorReason, errorText } from "../lib/api";
import { ExternalConfirm } from "./ExternalConfirm";
import type { HermesCheck } from "../gen/mlplatform/v1/labeling_pb";
import { Dialog, EmptyState, ErrorBox, Field, RelTime, Skeleton } from "./ui";
import { RunSheet } from "./AiActivity";
import { useSticky } from "../lib/persist";
import { translate as t, useT } from "../i18n";

/*
 * Hermes label checks in the UI. A check is never a bare verdict: every step (rules, decision model, auto-accept) is listed
 * with what it found and how long it took, the model call opens as a full AI run, and an automatic accept shows who can undo it.
 */

const VERDICT: Record<string, { label: string; cls: string; icon: typeof CheckCircle2 }> = {
  pass: { label: "Pass", cls: "bg-success/15 text-success", icon: CheckCircle2 },
  flag: { label: "Flagged", cls: "bg-warning/15 text-warning", icon: AlertTriangle },
  fail: { label: "Fail", cls: "bg-danger/15 text-danger", icon: CircleX },
  error: { label: "Check error", cls: "bg-surface-2 text-fg-muted", icon: CircleDashed },
};
const OUTCOME: Record<string, { cls: string; icon: typeof CheckCircle2 }> = {
  pass: { cls: "text-success", icon: CheckCircle2 },
  flag: { cls: "text-warning", icon: AlertTriangle },
  fail: { cls: "text-danger", icon: CircleX },
  skipped: { cls: "text-fg-subtle", icon: CircleDashed },
  error: { cls: "text-danger", icon: CircleX },
};
const KIND_ICON = { rule: ShieldCheck, model: BrainCircuit, action: Zap } as const;

export function VerdictBadge({ verdict, action }: { verdict: string; action?: string }) {
  const v = VERDICT[verdict] ?? VERDICT.error;
  return (
    <span className="inline-flex items-center gap-1">
      <span className={`badge ${v.cls}`}><v.icon className="h-3 w-3" />{t(v.label)}</span>
      {action === "auto_accepted" && <span className="badge bg-primary/15 text-primary"><Zap className="h-3 w-3" />{t("auto-accepted")}</span>}
      {action === "undone" && <span className="badge bg-surface-2 text-fg-muted"><RotateCcw className="h-3 w-3" />{t("undone")}</span>}
    </span>
  );
}

/** Steps, verdict, confidence, links. Used in the editor panel and in the check sheet. */
export function HermesCheckView({ c, ws, projectId, compact, onChanged }: { c: HermesCheck; ws: string; projectId: string; compact?: boolean; onChanged?: (c: HermesCheck) => void }) {
  const [run, setRun] = useState(false);
  const [explainRun, setExplainRun] = useState(false);
  const [confirmExt, setConfirmExt] = useState<string | null>(null);
  const tt = useT();
  const explain = useMutation({
    mutationFn: (confirm: boolean) => api.labeling.explainHermesCheck({ workspaceId: ws, projectId, checkId: c.id, lang: tt.lang, confirmExternal: confirm }),
    onSuccess: (x) => { setConfirmExt(null); onChanged?.(x); },
    onError: (e) => { if (errorReason(e) === "LABEL_EXTERNAL_CONFIRM") setConfirmExt(errorText(e)); },
  });
  const undo = useMutation({
    mutationFn: () => api.labeling.undoHermesAccept({ workspaceId: ws, projectId, checkId: c.id }),
    onSuccess: (x) => onChanged?.(x),
  });
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <VerdictBadge verdict={c.verdict} action={c.action} />
        {c.confidence >= 0 && <span className="text-xs tabular-nums text-fg-muted">{t("confidence")} {(c.confidence * 100).toFixed(0)}%</span>}
        <span className="ml-auto text-[11px] text-fg-subtle">{t(c.trigger === "submit" ? "on submit" : "manual check")} · <RelTime at={c.createdAt} /></span>
      </div>
      <div className={`text-sm ${compact ? "text-xs" : ""}`}>{t(c.summary)}</div>
      {c.suggestion && <div className="rounded-md border border-warning/30 bg-warning/10 px-2 py-1 text-xs text-warning">{t("Suggested class")}: <b>{c.suggestion}</b></div>}
      <ol className="space-y-1.5">
        {c.steps.map((s, i) => {
          const o = OUTCOME[s.outcome] ?? OUTCOME.skipped;
          const K = KIND_ICON[s.kind as keyof typeof KIND_ICON] ?? ShieldCheck;
          return (
            <li key={i} className="flex items-start gap-2 text-xs">
              <o.icon className={`mt-0.5 h-3.5 w-3.5 shrink-0 ${o.cls}`} />
              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-1 font-medium"><K className="h-3 w-3 text-fg-subtle" />{t(s.name)}
                  {s.ms > 0 && <span className="font-normal text-fg-subtle">· {s.ms} ms</span>}</span>
                {(s.outcome === "flag" || s.outcome === "fail") && s.detail && <span className="block break-words text-fg-muted">{s.detail.split("; ").map((x) => t(x)).join("; ")}</span>}
              </span>
            </li>);
        })}
      </ol>
      <div className="flex flex-wrap items-center gap-2 pt-1">
        {c.aiRunId && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setRun(true)}><BrainCircuit className="h-3.5 w-3.5" />{t("Open the AI call")}</button>}
        {c.canUndo && <button className="btn-ghost h-7 px-2 text-xs" disabled={undo.isPending} onClick={() => undo.mutate()}><RotateCcw className="h-3.5 w-3.5" />{t("Undo auto-accept")}</button>}
        {c.verdict !== "error" && <button className="btn-ghost h-7 px-2 text-xs" disabled={explain.isPending} onClick={() => explain.mutate(false)}>
          {explain.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Lightbulb className="h-3.5 w-3.5" />}{c.explanation ? t("Explain again") : t("Explain with AI")}</button>}
        {c.action === "undone" && <span className="text-[11px] text-fg-subtle">{t("Undone by {name}", { name: c.undoneByName })} · <RelTime at={c.undoneAt} /></span>}
      </div>
      {c.explanation && (
        <div className="rounded-lg border border-primary/30 bg-primary/5 p-2 text-xs">
          <div className="mb-1 flex items-center gap-1.5 font-medium text-primary"><Lightbulb className="h-3.5 w-3.5" />{t("AI explanation")}
            <span className="ml-auto font-normal text-fg-subtle"><RelTime at={c.explainedAt} /></span></div>
          <div className="whitespace-pre-wrap text-fg">{c.explanation}</div>
          {c.explanationRunId && <button className="mt-1 text-[11px] text-fg-subtle underline" onClick={() => setExplainRun(true)}>{t("Open the AI call")}</button>}
        </div>)}
      <ErrorBox error={undo.error ? errorText(undo.error) : explain.error && errorReason(explain.error) !== "LABEL_EXTERNAL_CONFIRM" ? errorText(explain.error) : null} />
      {explainRun && <RunSheet id={c.explanationRunId} onClose={() => setExplainRun(false)} />}
      {confirmExt && <ExternalConfirm message={confirmExt} busy={explain.isPending} onClose={() => setConfirmExt(null)} onConfirm={() => explain.mutate(true)}
        what={t("The analyst model receives the item's labels, the check steps, the guideline and — for text items — the item text.")} />}
      {run && <RunSheet id={c.aiRunId} onClose={() => setRun(false)} />}
    </div>
  );
}

/** Editor side panel: the latest check of this item, a "Check now" button, and the result of the previous submit. */
/** Result of the last submit, shown until dismissed (also after the queue runs empty). */
export function LastSubmitCard({ ws, projectId, check, onChange, onDismiss }:
  { ws: string; projectId: string; check: HermesCheck; onChange: (c: HermesCheck) => void; onDismiss: () => void }) {
  return (
    <div className="mb-2 rounded-lg border border-line bg-surface-2 p-2">
      <div className="mb-1 flex items-center justify-between text-[11px] text-fg-subtle">
        <span className="mono truncate">{t("Previous item")}: {check.path}</span>
        <button className="underline" onClick={onDismiss}>{t("Hide")}</button>
      </div>
      <HermesCheckView c={check} ws={ws} projectId={projectId} compact onChanged={onChange} />
    </div>
  );
}

export function HermesPanel({ ws, projectId, path, beforeCheck, readOnly, lastSubmit, onLastChanged, onDismissLast, etag, dirty }: {
  ws: string; projectId: string; path: string; beforeCheck: () => Promise<unknown>; readOnly: boolean;
  lastSubmit: HermesCheck | null; onLastChanged: (c: HermesCheck) => void; onDismissLast: () => void; etag: number; dirty: boolean;
}) {
  const qc = useQueryClient();
  const [open, setOpen] = useSticky<boolean>("hermes.panel.open", true);
  const key = ["hermes-item", ws, projectId, path];
  const latest = useQuery({ queryKey: key, queryFn: () => api.labeling.listHermesChecks({ workspaceId: ws, projectId, path, verdict: "", limit: 1 }) });
  const check = useMutation({
    mutationFn: async () => { await beforeCheck(); return api.labeling.checkItem({ workspaceId: ws, projectId, path }); },
    onSuccess: () => void qc.invalidateQueries({ queryKey: key }),
  });
  const c = latest.data?.checks[0];
  return (
    <div>
      <div className="mb-1.5 flex items-center justify-between">
        <button className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wider text-fg-subtle" onClick={() => setOpen(!open)} aria-expanded={open}>
          <Sparkles className="h-3.5 w-3.5 text-primary" />Hermes</button>
        {!readOnly && <button className="btn-ghost h-7 px-2 text-xs" disabled={check.isPending} onClick={() => check.mutate()}>
          {check.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <ShieldCheck className="h-3.5 w-3.5" />}{t("Check now")}</button>}
      </div>
      {lastSubmit && <LastSubmitCard ws={ws} projectId={projectId} check={lastSubmit} onChange={onLastChanged} onDismiss={onDismissLast} />}
      {open && (
        <>
          <ErrorBox error={check.error ? errorText(check.error) : latest.error ? errorText(latest.error) : null} />
          {check.isPending ? <div className="flex items-center gap-2 text-xs text-fg-muted"><Loader2 className="h-3.5 w-3.5 animate-spin" />{t("Saving, then checking rules and asking the decision model…")}</div>
            : c ? <>
              {(dirty || c.revision !== etag) && <div className="mb-2 rounded-md border border-warning/30 bg-warning/10 px-2 py-1 text-xs text-warning">
                {t("Labels changed since this check. Press “Check now” to check the current labels.")}</div>}
              <HermesCheckView c={c} ws={ws} projectId={projectId} compact onChanged={() => void qc.invalidateQueries({ queryKey: key })} /></>
            : <p className="text-xs text-fg-subtle">—</p>}
        </>)}
    </div>
  );
}

/* ───────── Project tab ───────── */

const FILTERS: [string, string][] = [["", "All"], ["fail", "Fail"], ["flag", "Flagged"], ["pass", "Pass"], ["auto_accepted", "Auto-accepted"], ["undone", "Undone"], ["error", "Check error"]];

export function HermesProjectTab({ ws, projectId }: { ws: string; projectId: string }) {
  const qc = useQueryClient();
  const stats = useQuery({ queryKey: ["hermes-stats", ws, projectId], queryFn: () => api.labeling.getHermesStats({ workspaceId: ws, projectId }), refetchInterval: 10_000 });
  const [filter, setFilter] = useSticky<string>(`hermes.filter.${projectId}`, "");
  const list = useQuery({ queryKey: ["hermes-checks", ws, projectId, filter], refetchInterval: 10_000,
    queryFn: () => api.labeling.listHermesChecks({ workspaceId: ws, projectId, verdict: filter, path: "", limit: 200 }) });
  const [open, setOpen] = useState<HermesCheck | null>(null);
  const [edit, setEdit] = useState(false);
  const s = stats.data;
  const p = s?.project;
  const refresh = () => { void qc.invalidateQueries({ queryKey: ["hermes-stats", ws, projectId] }); void qc.invalidateQueries({ queryKey: ["hermes-checks", ws, projectId] }); };
  if (stats.isLoading) return <Skeleton rows={5} />;
  return (
    <>
      <ErrorBox error={stats.error ? errorText(stats.error) : list.error ? errorText(list.error) : null} />
      {s && p && (
        <div className="mb-4 grid gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)]">
          <section className="card">
            <div className="mb-3 flex items-start justify-between gap-3">
              <div>
                <h2 className="flex items-center gap-2 text-sm font-semibold"><Sparkles className="h-4 w-4 text-primary" />{t("How Hermes works in this project")}</h2>
              </div>
              {s.canManage && <button className="btn-ghost" onClick={() => setEdit(true)}>{t("Change")}</button>}
            </div>
            <ul className="space-y-2 text-sm">
              <li className="flex gap-2"><span className={p.hermesEnabled ? "text-success" : "text-fg-subtle"}>●</span>
                <span>{p.hermesEnabled ? t("Every submitted item is checked automatically.") : t("Automatic checks are off. Annotators can still press “Check now”.")}</span></li>
              <li className="flex gap-2"><span className={p.autoAcceptThreshold > 0 && p.reviewMode === "all" ? "text-primary" : "text-fg-subtle"}>●</span>
                <span>{p.reviewMode !== "all" ? t("No review step: submitted items are accepted already, so Hermes only advises.")
                  : !p.hermesEnabled && p.autoAcceptThreshold > 0 ? t("Auto-accept (≥ {pct}) waits: it only works while automatic checks are on.", { pct: `${Math.round(p.autoAcceptThreshold * 100)}%` })
                  : p.autoAcceptThreshold > 0 ? t("Items that pass with model confidence ≥ {pct} are accepted without a reviewer. A Lead or Reviewer can undo.", { pct: `${Math.round(p.autoAcceptThreshold * 100)}%` })
                  : t("Auto-accept is off: every item still goes to a reviewer.")}</span></li>
              <li className="flex gap-2"><span className={s.decisionState === "ready" ? "text-success" : s.decisionState === "degraded" ? "text-warning" : "text-danger"}>●</span>
                <span>{s.decisionState === "missing" ? t("No decision model is ready, so checks use rules only.") : t("Decision model: {m}", { m: s.decisionModel })}
                  {s.decisionExternal && <span className="ml-1 badge bg-warning/15 text-warning">{t("external API")}</span>}
                  {s.decisionExternal && <span className="block text-xs text-warning">{t("Text items are sent to this external API when they are checked.")}</span>}
                  {s.decisionReasons.length > 0 && <span className="block text-xs text-fg-muted">{s.decisionReasons.map((r) => t(r)).join(" · ")}</span>}</span></li>
            </ul>
          </section>
          <section className="grid grid-cols-3 gap-2 sm:grid-cols-4">
            <Stat label={t("Checked")} value={s.checked} />
            <Stat label={t("Pass")} value={s.passed} tone="text-success" onClick={() => setFilter("pass")} />
            <Stat label={t("Flagged")} value={s.flagged} tone="text-warning" onClick={() => setFilter("flag")} />
            <Stat label={t("Fail")} value={s.failed} tone="text-danger" onClick={() => setFilter("fail")} />
            <Stat label={t("Auto-accepted")} value={s.autoAccepted} tone="text-primary" onClick={() => setFilter("auto_accepted")} />
            <Stat label={t("Undone")} value={s.undone} onClick={() => setFilter("undone")} />
            <Stat label={t("Check errors")} value={s.errors} onClick={() => setFilter("error")} />
            <div className="card px-3 py-2"><div className="text-xs text-fg-muted">{t("Avg. confidence")}</div>
              <div className="text-lg font-semibold tabular-nums">{s.avgConfidence >= 0 ? `${(s.avgConfidence * 100).toFixed(0)}%` : "—"}</div></div>
          </section>
        </div>)}

      <div className="mb-3 flex flex-wrap gap-1.5">
        {FILTERS.map(([k, l]) => <button key={k} onClick={() => setFilter(k)} className={`rounded-md px-2.5 py-1 text-sm ${filter === k ? "bg-surface-2 font-medium text-fg" : "text-fg-muted hover:text-fg"}`}>{t(l)}</button>)}
      </div>
      {list.isLoading ? <Skeleton rows={5} /> : (list.data?.checks.length ?? 0) === 0
        ? <EmptyState title={t("No check yet")} icon={<Sparkles className="h-6 w-6" />} />
        : (
          <div className="card divide-y divide-line p-0">
            {list.data!.checks.map((c) => (
              <button key={c.id} className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2.5 text-left text-sm hover:bg-surface-2" onClick={() => setOpen(c)}>
                <VerdictBadge verdict={c.verdict} action={c.action} />
                <span className="mono min-w-0 max-w-[40%] truncate">{c.path}</span>
                <span className="min-w-0 flex-1 truncate text-fg-muted">{t(c.summary)}</span>
                <span className="text-xs text-fg-subtle">{c.actorName} · <RelTime at={c.createdAt} /></span>
              </button>))}
          </div>)}
      {open && (
        <Dialog wide title={t("Hermes check")} description={<span className="mono">{open.path}</span>} onClose={() => setOpen(null)}>
          <HermesCheckView c={open} ws={ws} projectId={projectId} onChanged={(c) => { setOpen(c); refresh(); }} />
        </Dialog>)}
      {edit && p && <SettingsSheet ws={ws} projectId={projectId} enabled={p.hermesEnabled} threshold={p.autoAcceptThreshold} review={p.reviewMode}
        onClose={() => setEdit(false)} onSaved={() => { setEdit(false); refresh(); }} />}
    </>
  );
}

function Stat({ label, value, tone, onClick }: { label: string; value: bigint; tone?: string; onClick?: () => void }) {
  return (
    <button className="card px-3 py-2 text-left disabled:cursor-default" onClick={onClick} disabled={!onClick}>
      <div className="text-xs text-fg-muted">{label}</div>
      <div className={`text-lg font-semibold tabular-nums ${tone ?? ""}`}>{String(value)}</div>
    </button>
  );
}

function SettingsSheet({ ws, projectId, enabled, threshold, review, onClose, onSaved }:
  { ws: string; projectId: string; enabled: boolean; threshold: number; review: string; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ enabled, threshold });
  const save = useMutation({ mutationFn: () => api.labeling.setHermesSettings({ workspaceId: ws, projectId, enabled: f.enabled, autoAcceptThreshold: f.threshold }), onSuccess: onSaved });
  return (
    <Dialog title={t("Hermes settings")} onClose={onClose}>
      <div className="space-y-4">
        <ErrorBox error={save.error ? errorText(save.error) : null} />
        <label className="flex items-start gap-2 text-sm">
          <input type="checkbox" className="mt-1" checked={f.enabled} onChange={(e) => setF({ ...f, enabled: e.target.checked })} />
          <span>{t("Check every submitted item automatically")}</span>
        </label>
        <Field label={t("Auto-accept threshold")}>
          <div className="flex items-center gap-3">
            <input type="range" min={0} max={1} step={0.01} value={f.threshold} className="flex-1" disabled={!f.enabled || review !== "all"}
              onChange={(e) => setF({ ...f, threshold: Number(e.target.value) })} />
            <span className="w-16 text-right text-sm tabular-nums">{f.threshold === 0 ? t("off") : `≥ ${Math.round(f.threshold * 100)}%`}</span>
          </div>
        </Field>
        <div className="flex justify-end gap-2 border-t border-line pt-4">
          <button className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-primary" disabled={save.isPending} onClick={() => save.mutate()}>{save.isPending ? t("Saving…") : t("Save")}</button>
        </div>
      </div>
    </Dialog>
  );
}
