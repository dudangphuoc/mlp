import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, ArrowRight, BrainCircuit, CheckCircle2, CircleX, Clock, Coins, Loader2, Shuffle } from "lucide-react";
import { api, errorText } from "../lib/api";
import type { AiRun } from "../gen/mlplatform/v1/ai_pb";
import { CommandBlock, Dialog, ErrorBox, RelTime, Skeleton } from "./ui";
import { translate as t } from "../i18n";

/*
 * Transparency for every AI call (Hermes). Nothing runs silently:
 * - the header indicator shows what AI is doing right now and what failed recently;
 * - the activity sheet lists the latest runs;
 * - the run sheet shows the whole record: why this model, every attempt, exact input and output, cost, fallback.
 */

export const ROLE_LABEL: Record<string, string> = {
  analyst: "Analyst", decision: "Decision", judge: "Judge", vision: "Vision", labeler: "Labeler", embed: "Embedding", draft: "Draft",
};

const TRIGGER_LABEL: Record<string, string> = { manual: "Started by a person", auto: "Automatic", "auto-recovery": "Automatic retest", rule: "Rule" };
const DECIDED_LABEL: Record<string, string> = { model: "Model", heuristic: "Built-in rules", fallback: "Fallback model", none: "Nobody (failed)" };
const OUTCOME_LABEL: Record<string, string> = {
  ok: "OK", timeout: "Timed out", unreachable: "Unreachable", "rate-limited": "Rate limited", auth: "Key rejected", rejected: "Request rejected",
  "bad-response": "Invalid answer", "circuit-open": "Paused (circuit open)", busy: "All slots busy", "not-found": "Model not found", "server-error": "Provider error", "empty-answer": "Empty answer",
};

export const fmtMs = (ms: number) => (ms >= 10_000 ? `${(ms / 1000).toFixed(1)} s` : `${ms} ms`);
export const fmtCost = (usd: number) => (usd < 0 ? t("unknown price") : usd === 0 ? "$0" : usd < 0.01 ? `$${usd.toFixed(5)}` : `$${usd.toFixed(3)}`);

/** Small status icon for a run row. */
export function RunIcon({ run }: { run: AiRun }) {
  if (run.state === "running") return <Loader2 className="h-4 w-4 shrink-0 animate-spin text-info" aria-label={t("running")} />;
  if (run.state === "failed") return <CircleX className="h-4 w-4 shrink-0 text-danger" aria-label={t("failed")} />;
  if (run.fallback) return <Shuffle className="h-4 w-4 shrink-0 text-warning" aria-label={t("Fallback")} />;
  return <CheckCircle2 className="h-4 w-4 shrink-0 text-success" aria-label={t("done")} />;
}

/** One line of the run timeline: what, which model, result, latency. */
export function RunRow({ run, onOpen, showSubject = true }: { run: AiRun; onOpen: (id: string) => void; showSubject?: boolean }) {
  return (
    <button className="flex w-full items-start gap-3 rounded-lg px-2 py-2 text-left hover:bg-surface-2" onClick={() => onOpen(run.id)}>
      <span className="mt-0.5"><RunIcon run={run} /></span>
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-x-2 text-sm">
          <span className="font-medium">{t(run.purpose)}</span>
          {showSubject && run.subject && <span className="truncate text-fg-muted">{run.subject}</span>}
        </span>
        <span className={`block truncate text-xs ${run.state === "failed" ? "text-danger" : "text-fg-muted"}`}>{t(run.summary)}</span>
        <span className="mt-0.5 flex flex-wrap items-center gap-x-3 text-[11px] text-fg-subtle">
          <span>{run.modelLabel || t("no model")}</span>
          {run.decidedBy === "heuristic" && <span className="text-warning">{t("answered by built-in rules")}</span>}
          {run.fallback && run.decidedBy !== "heuristic" && <span className="text-warning">{t("fallback used")}</span>}
          {run.state !== "running" && <span>{fmtMs(run.latencyMs)}</span>}
          <span>{t(TRIGGER_LABEL[run.trigger] ?? run.trigger)}{run.actorName ? ` · ${run.actorName}` : ""}</span>
          <RelTime at={run.startedAt} />
        </span>
      </span>
      <ArrowRight className="mt-1 h-3.5 w-3.5 shrink-0 text-fg-subtle" />
    </button>
  );
}

function Section({ title, children, right }: { title: string; children: ReactNode; right?: ReactNode }) {
  return (
    <section className="mb-5">
      <div className="mb-2 flex items-center justify-between"><h3 className="text-xs font-semibold uppercase tracking-wider text-fg-subtle">{title}</h3>{right}</div>
      {children}
    </section>
  );
}

function pretty(json: string): string {
  try { return JSON.stringify(JSON.parse(json), null, 2); } catch { return json; }
}

type Answer = { choice?: string; score?: number; noul?: number; confidence?: number; probabilities?: Record<string, number> };

/** Typed decision answers: choice + confidence + probability bars per option. */
function Answers({ input, output }: { input: string; output: string }) {
  let answers: Record<string, Answer> = {};
  let questions: { id: string; instructions: string; options: { key: string; description: string }[] }[] = [];
  try { answers = JSON.parse(output).answers ?? {}; } catch { /* not a decision */ }
  try { questions = JSON.parse(input).questions ?? []; } catch { /* not a decision */ }
  const ids = Object.keys(answers);
  if (ids.length === 0) return null;
  return (
    <div className="space-y-3">
      {ids.map((id) => {
        const a = answers[id];
        const q = questions.find((x) => x.id === id);
        const value = a.choice ?? a.score ?? a.noul;
        const probs = Object.entries(a.probabilities ?? {}).sort((x, y) => y[1] - x[1]);
        return (
          <div key={id} className="rounded-lg border border-line p-3">
            <div className="text-xs text-fg-muted">{q?.instructions ?? id}</div>
            <div className="mt-1 flex flex-wrap items-baseline gap-2">
              <span className="text-base font-semibold">{String(value)}</span>
              {q?.options.find((o) => o.key === a.choice)?.description && <span className="text-sm text-fg-muted">— {q.options.find((o) => o.key === a.choice)!.description}</span>}
              {a.confidence !== undefined && <span className="ml-auto text-sm tabular-nums">{t("confidence")} {(a.confidence * 100).toFixed(0)}%</span>}
            </div>
            {probs.length > 0 && (
              <div className="mt-2 space-y-1">
                {probs.map(([k, p]) => (
                  <div key={k} className="flex items-center gap-2 text-xs">
                    <span className="w-20 truncate text-fg-muted">{k}</span>
                    <span className="h-1.5 flex-1 overflow-hidden rounded-full bg-surface-2"><span className={`block h-full ${k === a.choice ? "bg-primary" : "bg-fg-subtle/50"}`} style={{ width: `${Math.max(1, p * 100)}%` }} /></span>
                    <span className="w-10 text-right tabular-nums">{(p * 100).toFixed(0)}%</span>
                  </div>))}
              </div>)}
          </div>);
      })}
    </div>
  );
}

/** Full record of one AI call. */
export function RunSheet({ id, onClose }: { id: string; onClose: () => void }) {
  const q = useQuery({ queryKey: ["ai-run", id], queryFn: () => api.ai.getRun({ id }),
    refetchInterval: (query) => (query.state.data?.state === "running" ? 1500 : false) });
  const r = q.data;
  let text = "", reasoning = "";
  try { const o = r ? JSON.parse(r.outputJson || "{}") : {}; text = o.text ?? ""; reasoning = o.reasoning ?? ""; } catch { /* decision output */ }
  return (
    <Dialog wide title={r ? t(r.purpose) : t("AI call")} onClose={onClose}
      description={r ? <span>{r.subject && <>{r.subject} · </>}{t(TRIGGER_LABEL[r.trigger] ?? r.trigger)}{r.actorName ? ` · ${r.actorName}` : ""} · <RelTime at={r.startedAt} /></span> : undefined}>
      <ErrorBox error={q.error ? errorText(q.error) : null} onRetry={() => void q.refetch()} />
      {!r ? <Skeleton rows={6} /> : (
        <>
          <div className="mb-5 grid grid-cols-2 gap-2 sm:grid-cols-4">
            <Metric icon={<RunIcon run={r} />} label={t("Result")} value={r.state === "running" ? t("running") : r.state === "failed" ? t("failed") : t(DECIDED_LABEL[r.decidedBy] ?? r.decidedBy)} />
            <Metric icon={<Clock className="h-4 w-4 text-fg-subtle" />} label={t("Time")} value={r.state === "running" ? "…" : fmtMs(r.latencyMs)} />
            <Metric icon={<BrainCircuit className="h-4 w-4 text-fg-subtle" />} label={t("Tokens")} value={`${r.tokensIn} → ${r.tokensOut}`} />
            <Metric icon={<Coins className="h-4 w-4 text-fg-subtle" />} label={t("Cost")} value={fmtCost(r.costUsd)} />
          </div>
          {r.fallback && (
            <div className="mb-4 flex gap-2 rounded-lg border border-amber-300/50 bg-amber-500/10 px-3 py-2 text-sm text-warning">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /><span><b>{t("The first choice was not used.")}</b> {t(r.fallback)}</span>
            </div>)}
          {r.state === "failed" && (
            <div className="mb-4 rounded-lg border border-rose-300/50 bg-rose-500/10 px-3 py-2 text-sm text-danger">
              <b>{r.errorCode}</b> {t(r.error)}
            </div>)}

          <Section title={t("Answer")}>
            {r.state === "running" ? <div className="flex items-center gap-2 text-sm text-fg-muted"><Loader2 className="h-4 w-4 animate-spin" />{t("Waiting for the model…")}</div>
              : <><Answers input={r.inputJson} output={r.outputJson} />
                {text && <div className="whitespace-pre-wrap rounded-lg border border-line bg-surface-2 p-3 text-sm">{text}</div>}
                {!text && r.state === "succeeded" && !r.outputJson.includes("answers") && <div className="text-sm text-fg-muted">{t(r.summary)}</div>}
                {r.confidence >= 0 && <div className="mt-2 text-xs text-fg-muted">{t("Lowest confidence across answers")}: {(r.confidence * 100).toFixed(0)}%</div>}</>}
          </Section>

          {reasoning && <Section title={t("Model reasoning")}><div className="max-h-60 overflow-y-auto whitespace-pre-wrap rounded-lg border border-line p-3 text-xs text-fg-muted">{reasoning}</div></Section>}

          <Section title={t("Why this model")}>
            <p className="mb-2 text-sm">{t(r.selectionNote)}</p>
            <div className="overflow-hidden rounded-lg border border-line">
              {r.candidates.length === 0 && <div className="px-3 py-2 text-sm text-fg-muted">{t("No model is declared yet.")}</div>}
              {r.candidates.map((c) => (
                <div key={c.modelId} className={`flex items-start gap-3 border-b border-line px-3 py-2 text-sm last:border-0 ${c.eligible ? "" : "text-fg-subtle"}`}>
                  <span className={`mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[11px] font-semibold ${c.rank === 1 ? "bg-primary/20 text-primary" : c.eligible ? "bg-surface-2" : "bg-surface-2 text-fg-subtle"}`}>{c.rank || "–"}</span>
                  <span className="min-w-0 flex-1"><span className={c.eligible ? "font-medium" : ""}>{c.modelLabel}</span>
                    <span className="block text-xs text-fg-muted">{c.reason.split(" · ").map((x) => t(x)).join(" · ")}</span></span>
                </div>))}
            </div>
          </Section>

          <Section title={t("Attempts")}>
            {r.attempts.length === 0 ? <div className="text-sm text-fg-muted">{r.state === "running" ? t("In progress…") : t("No call was made.")}</div> : (
              <ol className="space-y-2">
                {r.attempts.map((a) => (
                  <li key={a.number} className="flex items-start gap-3 text-sm">
                    <span className={`mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[11px] ${a.outcome === "ok" ? "bg-success/20 text-success" : "bg-danger/20 text-danger"}`}>{a.number}</span>
                    <span className="min-w-0 flex-1">
                      <span className="font-medium">{a.modelLabel}</span> · <span className={a.outcome === "ok" ? "text-success" : "text-danger"}>{t(OUTCOME_LABEL[a.outcome] ?? a.outcome)}</span>
                      <span className="text-fg-muted"> · {fmtMs(a.latencyMs)}{a.status ? ` · HTTP ${a.status}` : ""}</span>
                      {a.detail && <span className="block break-words text-xs text-fg-muted">{t(a.detail)}</span>}
                    </span>
                  </li>))}
              </ol>)}
          </Section>

          <Section title={t("Exact input sent")}><CommandBlock text={pretty(r.inputJson)} /></Section>
          {r.state !== "running" && <Section title={t("Exact output received")}><CommandBlock text={pretty(r.outputJson)} /></Section>}
          <p className="text-xs text-fg-subtle">ID {r.id}</p>
        </>)}
    </Dialog>
  );
}

function Metric({ icon, label, value }: { icon: ReactNode; label: string; value: ReactNode }) {
  return (
    <div className="rounded-lg border border-line px-3 py-2">
      <div className="flex items-center gap-1.5 text-xs text-fg-muted">{icon}{label}</div>
      <div className="mt-0.5 text-sm font-semibold tabular-nums">{value}</div>
    </div>
  );
}

/** Header indicator: running / failed / fallback counts; opens the activity sheet. */
export function AiActivityButton({ ws, isAdmin, go }: { ws: string; isAdmin: boolean; go: (p: string) => void }) {
  const [open, setOpen] = useState(false);
  const [run, setRun] = useState<string | null>(null);
  const q = useQuery({ queryKey: ["ai-activity", ws], queryFn: () => api.ai.getActivity({ workspaceId: ws }), refetchInterval: open ? 3000 : 15000, retry: false });
  const a = q.data;
  const running = a?.running ?? 0, failed = a?.failedRecent ?? 0, fb = a?.fallbackRecent ?? 0;
  if (!a && !open) return null;   // no permission or AI not set up: no indicator
  const label = running > 0 ? t("AI: {n} running", { n: running }) : failed > 0 ? t("AI: {n} failed in the last hour", { n: failed }) : t("AI activity");
  return (
    <>
      <button className="relative inline-flex h-8 items-center gap-1.5 rounded-md px-2 text-sm text-fg-muted hover:bg-surface-2 hover:text-fg" onClick={() => setOpen(true)} aria-label={label} title={label}>
        <BrainCircuit className={`h-4 w-4 ${running > 0 ? "text-info" : ""}`} aria-hidden />
        <span className="hidden lg:inline">AI</span>
        {running > 0 && <span className="flex items-center gap-1 rounded-full bg-info/20 px-1.5 text-[11px] font-semibold leading-4 text-info"><span className="h-1.5 w-1.5 animate-pulse rounded-full bg-current" />{running}</span>}
        {running === 0 && failed > 0 && <span className="rounded-full bg-danger px-1.5 text-[11px] font-semibold leading-4 text-white">{failed}</span>}
        {running === 0 && failed === 0 && fb > 0 && <span className="rounded-full bg-warning/80 px-1.5 text-[11px] font-semibold leading-4 text-white">{fb}</span>}
      </button>
      {open && (
        <Dialog wide title={t("AI activity")} onClose={() => setOpen(false)}>
          <div className="mb-4 grid grid-cols-3 gap-2">
            <Metric icon={<Loader2 className={`h-4 w-4 ${running ? "animate-spin text-info" : "text-fg-subtle"}`} />} label={t("Running now")} value={running} />
            <Metric icon={<CircleX className="h-4 w-4 text-danger" />} label={t("Failed (1 h)")} value={failed} />
            <Metric icon={<Shuffle className="h-4 w-4 text-warning" />} label={t("Fallback (1 h)")} value={fb} />
          </div>
          <ErrorBox error={q.error ? errorText(q.error) : null} onRetry={() => void q.refetch()} />
          {!a ? <Skeleton rows={5} /> : a.recent.length === 0
            ? <div className="py-10 text-center text-sm text-fg-muted">{t("No AI call yet.")}</div>
            : <div className="-mx-2">{a.recent.map((r) => <RunRow key={r.id} run={r} onOpen={setRun} />)}</div>}
          {isAdmin && <button className="btn-ghost mt-4 w-full" onClick={() => { setOpen(false); go("ai?tab=runs"); }}>{t("Open the full AI log")}</button>}
        </Dialog>)}
      {run && <RunSheet id={run} onClose={() => setRun(null)} />}
    </>
  );
}
