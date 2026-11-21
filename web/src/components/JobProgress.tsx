import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowDownToLine, Download, Search } from "lucide-react";
import { api } from "../lib/api";
import { clock } from "../lib/format";
import { translate as t } from "../i18n";
import { useLive } from "../pages/Ops";
import type { JobProgress, LogLine, OpsEvent } from "../gen/mlplatform/v1/scheduler_pb";

/* Shared by the job page and the run page: step bar, progress line, log panel, event timeline (20/10 §3). */

export const PHASES: [string, string][] = [
  ["queued", "Queued"], ["reserving", "Reserving resources"], ["preparing_data", "Preparing data"], ["restoring_checkpoint", "Restoring checkpoint"],
  ["running", "Run"], ["uploading", "Uploading results"], ["finishing", "Done"],
];

export function secs(ts?: { seconds: bigint }) { return ts ? Number(ts.seconds) : 0; }

export function dur(s: number) {
  if (s <= 0 || !Number.isFinite(s)) return "—";
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), x = Math.floor(s % 60);
  return h ? t("{h} h {m} min", { h, m }) : m ? t("{m} min {x} s", { m, x }) : t("{x} s", { x });
}

/** Seconds that tick once per second, for "x s ago" and running durations. */
export function useNow() {
  const [now, setNow] = useState(Date.now() / 1000);
  useEffect(() => { const i = setInterval(() => setNow(Date.now() / 1000), 1000); return () => clearInterval(i); }, []);
  return now;
}

/** Which standard step the job is in, from its state and reported progress. */
export function currentPhase(state: string, p?: JobProgress): string {
  if (state === "queued" || state === "scheduling") return "queued";
  if (state === "starting" && !p?.phase) return "reserving";
  return p?.phase ?? "";
}

export function PhaseBar({ state, progress }: { state: string; progress?: JobProgress }) {
  const live = ["queued", "scheduling", "starting", "running"].includes(state);
  const idx = PHASES.findIndex(([k]) => k === currentPhase(state, progress));
  return (
    <div className="flex flex-wrap gap-1">
      {PHASES.map(([k, label], i) => (
        <div key={k} className={`flex-1 whitespace-nowrap rounded px-2 py-1 text-center text-xs ${
          state === "succeeded" || i < idx ? "bg-emerald-100 text-emerald-800"
            : i === idx && live ? "bg-sky-100 font-medium text-sky-800" : "bg-surface-2 text-fg-subtle"}`}>{t(label)}</div>))}
    </div>
  );
}

/** "12/24 epoch · 50% · about 6 min left" — or the step's own message while it has no measurable total. */
export function progressText(p: JobProgress): string {
  const parts: string[] = [];
  if (p.total > 0) parts.push(`${fmt(p.current)}/${fmt(p.total)} ${t(p.unit)}`.trim(), `${p.percent.toFixed(0)}%`);
  else if (p.message) parts.push(t(p.message));
  if (p.etaSeconds > 0) parts.push(t("about {d} left", { d: dur(p.etaSeconds) }));
  return parts.join(" · ");
}
const fmt = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(1));

export function ProgressLine({ progress, compact }: { progress: JobProgress; compact?: boolean }) {
  const known = progress.total > 0;
  return (
    <div>
      <div className={`overflow-hidden rounded-full bg-surface-2 ${compact ? "h-1.5" : "h-2"}`}>
        {/* No measurable total: a dim pulsing bar says "working", never "100 %". */}
        <div className={`h-full bg-info transition-all ${known ? "" : "animate-pulse opacity-40"}`} style={{ width: `${known ? Math.min(100, Math.max(2, progress.percent)) : 100}%` }} />
      </div>
      <div className={`mt-1 text-xs text-fg-muted ${compact ? "truncate" : ""}`}>
        {progressText(progress) || t("Working — this step reports no percentage")}{!compact && progress.rate > 0 && ` · ${progress.rate.toFixed(2)} ${t(progress.unit)}/s`}
      </div>
    </div>
  );
}

/** Stored lines of a job plus the lines pushed while the page is open (the page must be subscribed to the job's stream). */
export function useJobLog(ws: string, jobId: string | undefined, tail = 1000): LogLine[] {
  const stream = useLive();
  useEffect(() => { if (!jobId) return; stream.subscribe(jobId); return () => stream.subscribe(null); }, [jobId]); // eslint-disable-line react-hooks/exhaustive-deps
  const initial = useQuery({
    queryKey: ["job-logs", ws, jobId, tail], enabled: !!jobId, placeholderData: (old) => old,
    queryFn: () => api.scheduler.getJobLogs({ workspaceId: ws, jobId: jobId!, attemptId: "", tail, afterTsMs: 0n }),
  });
  return useMemo(() => {
    const seen = new Set<string>();
    return [...(initial.data?.lines ?? []), ...stream.logs]
      .filter((l) => { const k = `${l.attemptId}|${l.tsMs}|${l.text}`; if (seen.has(k)) return false; seen.add(k); return true; })
      .sort((a, b) => Number(a.tsMs - b.tsMs)).slice(-(tail + 3000));
  }, [initial.data, stream.logs, tail]);
}

const ERROR_LINE = /\b(error|exception|traceback|fatal|killed|out of memory|oom|failed|no such file|permission denied)\b/i;
// Last line of a Python traceback ("[rank0]: ModuleNotFoundError: No module named 'x'"); launcher wrappers only say that a child failed.
const EXCEPTION_LINE = /^(?:\[[^\]]{1,24}\]:?\s*)?((?:[A-Za-z_][\w.]*\.)?[A-Z]\w*(?:Error|Exception|Interrupt)|KeyboardInterrupt)(?::\s*(.*))?$/;
const WRAPPERS = /(ChildFailedError|CalledProcessError|ProcessRaisedException|ProcessExitedException|SystemExit)$/;

/**
 * What explains a failure: the error the program itself raised (`headline`) and the traceback lines leading to it.
 * Only the last attempt counts, and launcher wrappers (torchrun's ChildFailedError…) are skipped — they hide the real cause.
 */
export function errorExcerpt(all: LogLine[], max = 14): { headline: string; lines: string[] } {
  const lastAttempt = all[all.length - 1]?.attemptId;
  const text = all.filter((l) => l.attemptId === lastAttempt).map((l) => l.text);
  const from = Math.max(0, text.length - 600);
  for (let i = text.length - 1; i >= from; i--) {
    const m = EXCEPTION_LINE.exec(text[i].trimEnd());
    if (!m || WRAPPERS.test(m[1])) continue;
    let start = i;
    for (let j = i - 1; j >= Math.max(0, i - 80); j--) if (/Traceback \(most recent call last\)/.test(text[j])) { start = j; break; }
    const block = text.slice(start, i + 1);
    return { headline: m[2] ? `${m[1]}: ${m[2].trim()}` : m[1], lines: block.length > max ? [block[0], "  …", ...block.slice(-(max - 2))] : block };
  }
  let start = -1;
  for (let i = text.length - 1; i >= Math.max(0, text.length - 60); i--) if (ERROR_LINE.test(text[i])) start = i;
  return { headline: "", lines: start < 0 ? text.slice(-6) : text.slice(start).slice(-max) };
}

/** Log viewer: follows the tail while it is at the bottom, filter by text, download what is loaded. */
export function LogPanel({ lines, height = "18rem", name = "job", onLoadEarlier }: { lines: LogLine[]; height?: string; name?: string; onLoadEarlier?: () => void }) {
  const box = useRef<HTMLPreElement>(null);
  const [q, setQ] = useState("");
  const [follow, setFollow] = useState(true);
  const shown = useMemo(() => { const s = q.trim().toLowerCase(); return s ? lines.filter((l) => l.text.toLowerCase().includes(s)) : lines; }, [lines, q]);
  useEffect(() => { if (follow && box.current) box.current.scrollTop = box.current.scrollHeight; }, [shown.length, follow]);
  const download = () => {
    const body = lines.map((l) => `${new Date(Number(l.tsMs)).toISOString()}  ${l.text}`).join("\n");
    const url = URL.createObjectURL(new Blob([body], { type: "text/plain;charset=utf-8" }));
    Object.assign(document.createElement("a"), { href: url, download: `${name}.log` }).click();
    URL.revokeObjectURL(url);
  };
  return (
    <div className="card">
      <div className="mb-2 flex flex-wrap items-center gap-2">
        <div className="text-sm font-medium">{t("Log")}</div>
        <span className="text-xs text-fg-subtle">{q ? t("{n} of {m} lines", { n: shown.length, m: lines.length }) : t("{n} lines", { n: lines.length })}</span>
        {onLoadEarlier && <button className="text-xs text-primary" onClick={onLoadEarlier}>{t("Load earlier lines")}</button>}
        <div className="relative ml-auto">
          <Search className="pointer-events-none absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-fg-subtle" />
          <input className="input h-8 w-56 pl-7" placeholder={t("Filter lines…")} value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <button className={`btn-ghost h-8 px-2 ${follow ? "text-primary" : ""}`} title={t("Follow new lines")} aria-pressed={follow} onClick={() => setFollow(!follow)}>
          <ArrowDownToLine className="h-4 w-4" />{t("Follow")}</button>
        <button className="btn-ghost h-8 px-2" title={t("Download the loaded lines")} disabled={lines.length === 0} onClick={download}><Download className="h-4 w-4" /></button>
      </div>
      <pre ref={box} className="mono overflow-auto rounded-md bg-[#0b0e13] p-3 text-[11px] leading-[1.45]" style={{ maxHeight: height, minHeight: "6rem", color: "#d5dae3" }}
        onScroll={(e) => { const el = e.currentTarget; const atEnd = el.scrollHeight - el.scrollTop - el.clientHeight < 24; if (!atEnd && follow) setFollow(false); }}>
        {shown.length === 0 ? <span style={{ color: "#6b7484" }}>{lines.length === 0 ? t("No log lines yet.") : t("No line matches the filter.")}</span>
          : shown.map((l, i) => (
            <div key={i} className={ERROR_LINE.test(l.text) ? "text-rose-500" : undefined}>
              <span style={{ color: "#6b7484" }}>{clock(Number(l.tsMs))}</span>{"  "}{l.text}
            </div>))}
      </pre>
    </div>
  );
}

const sevTone: Record<string, string> = { info: "bg-sky-500", success: "bg-emerald-500", warning: "bg-amber-400", error: "bg-rose-500" };
const srcTone = (s: string) => (s.startsWith("agent") ? "text-violet-600" : s === "shim" ? "text-sky-700" : s === "holder" ? "text-amber-700" : "text-fg-muted");

/** Who did what, when — scheduler, agent, shim (20/10 §2.2). `limit` shows only the newest entries. */
export function EventList({ events, limit }: { events: OpsEvent[]; limit?: number }) {
  const list = limit ? events.slice(-limit) : events;
  if (events.length === 0) return <div className="text-xs text-fg-subtle">{t("No events yet.")}</div>;
  return (
    <ol className="space-y-1.5">
      {list.map((e) => (
        <li key={e.id} className="flex items-start gap-2 text-xs">
          <span className={`mt-1 h-2 w-2 shrink-0 rounded-full ${sevTone[e.severity] ?? "bg-slate-300"}`} />
          <span className="w-20 shrink-0 tabular-nums text-fg-subtle">{e.at ? clock(secs(e.at) * 1000) : ""}</span>
          <span className={`w-20 shrink-0 font-mono ${srcTone(e.source)}`}>{e.source.startsWith("agent:") ? "agent" : e.source}</span>
          <span className="min-w-0 break-words">{t(e.message)}</span>
        </li>))}
    </ol>
  );
}

/** Older jobs stored a bare "failed: " with no message; say something useful instead of an empty reason. */
export function reasonText(reason: string): string {
  if (!reason) return "";
  if (/^failed:?\s*$/.test(reason)) return t("The process failed — open the log to see why");
  // "loss_invalid: The loss became NaN …" (reason code: message) — translate both parts.
  const m = /^([a-z][a-z0-9_]*): ([\s\S]+)$/.exec(reason);
  if (m) return `${t(m[1])}: ${t(m[2])}`;
  return t(reason);
}
