import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ago } from "../lib/format";
import { Square } from "lucide-react";
import { ConfirmDialog, ErrorBox, PageHeader, RelTime, ShortId, StateBadge } from "../components/ui";
import type { Job } from "../gen/mlplatform/v1/scheduler_pb";
import { DataTable } from "../components/DataTable";
import { dur, EventList, LogPanel, PhaseBar, ProgressLine, reasonText, secs, useJobLog, useNow } from "../components/JobProgress";
import { translate as t } from "../i18n";
import { DataSyncPanel } from "../components/DataSync";
import { useCrumb } from "../lib/crumbs";
import { readSticky, writeSticky } from "../lib/persist";

const ACTIVE = ["queued", "scheduling", "starting", "running"];

/** Stop any job that has not finished (waiting, reserving, preparing, running); whatever owns it — run, deployment, notebook — follows. */
function useCancelJob(ws: string) {
  const qc = useQueryClient();
  const [target, setTarget] = useState<Job | null>(null);
  const cancel = useMutation({
    mutationFn: (j: Job) => api.scheduler.cancelJob({ workspaceId: ws, jobId: j.id }),
    onSuccess: () => { setTarget(null); for (const k of ["jobs", "job", "job-ops", "runs", "deployments", "notebooks"]) void qc.invalidateQueries({ queryKey: [k, ws] }); },
  });
  const sheet = target && (
    <ConfirmDialog title={t("Stop job {name}?", { name: target.name })} label="Stop" busy={cancel.isPending} onClose={() => { setTarget(null); cancel.reset(); }}
      onConfirm={() => cancel.mutate(target)} consequence={cancel.error ? <ErrorBox error={errorText(cancel.error)} /> : null} />);
  return { ask: setTarget, sheet };
}

/** S-03-1 Jobs (M03). */
export function JobsPage({ ws, onOpen }: { ws: string; onOpen: (id: string) => void }) {
  const jobs = useQuery({ queryKey: ["jobs", ws], queryFn: () => api.scheduler.listJobs({ workspaceId: ws }), refetchInterval: 4000 });
  const list = jobs.data?.jobs ?? [];
  const [state, setState] = useState(() => new URLSearchParams(window.location.search).get("state") ?? readSticky("jobs:state", ""));
  const [kind, setKind] = useState(() => new URLSearchParams(window.location.search).get("kind") ?? readSticky("jobs:kind", ""));
  const setUrl = (k: string, v: string) => { writeSticky("jobs:" + k, v); const u = new URL(window.location.href); if (v) u.searchParams.set(k, v); else u.searchParams.delete(k); window.history.replaceState(window.history.state, "", u); };
  const rows = list.filter((j) => (!state || j.state === state) && (!kind || j.kind === kind));
  const kinds = [...new Set(list.map((j) => j.kind))].sort();
  const active = list.filter((j) => ACTIVE.includes(j.state)).length;
  const stop = useCancelJob(ws);
  return (
    <>
      <PageHeader title="Jobs" subtitle={t("{n} active", { n: active })} />
      <ErrorBox error={jobs.error ? errorText(jobs.error) : null} onRetry={() => void jobs.refetch()} />
      <DataTable rows={rows} loading={jobs.isLoading} rowKey={(j) => j.id} onRowClick={(j) => onOpen(j.id)}
        empty={t("No jobs yet")}
        actions={<>
          <select className="input h-8 w-auto" value={state} onChange={(e) => { setState(e.target.value); setUrl("state", e.target.value); }}>
            <option value="">{t("Status: all")}</option>{["queued", "running", "succeeded", "failed", "cancelled"].map((x) => <option key={x} value={x}>{x}</option>)}
          </select>
          <select className="input h-8 w-auto" value={kind} onChange={(e) => { setKind(e.target.value); setUrl("kind", e.target.value); }}>
            <option value="">{t("Kind: all")}</option>{kinds.map((x) => <option key={x} value={x}>{x}</option>)}
          </select>
        </>}
        columns={[
          { key: "name", header: t("Name"), value: (j) => j.name, cell: (j) => <span className="font-medium">{j.name}</span> },
          { key: "id", header: "ID", cell: (j) => <ShortId id={j.id} />, hidden: true },
          { key: "kind", header: t("Kind"), value: (j) => j.kind, cell: (j) => <span className="text-fg-muted">{j.kind}</span> },
          { key: "state", header: t("Status"), value: (j) => j.state, cell: (j) => <StateBadge state={j.state} reason={j.stateReason || undefined} /> },
          { key: "reason", header: t("Reason"), value: (j) => j.stateReason, cell: (j) => <span className="block max-w-md truncate text-xs text-fg-muted" title={reasonText(j.stateReason)}>{reasonText(j.stateReason)}</span> },
          { key: "attempts", header: "Attempt", align: "right", value: (j) => j.attemptCount, cell: (j) => j.attemptCount },
          { key: "dur", header: t("Duration"), align: "right", value: (j) => (j.startedAt ? (secs(j.finishedAt) || Date.now() / 1000) - secs(j.startedAt) : 0),
            cell: (j) => <span className="whitespace-nowrap text-xs text-fg-muted">{j.startedAt ? dur((secs(j.finishedAt) || Date.now() / 1000) - secs(j.startedAt)) : "—"}</span> },
          { key: "created", header: t("Created"), value: (j) => secs(j.createdAt), cell: (j) => <span className="text-xs"><RelTime at={j.createdAt} /></span> },
          { key: "act", header: "", width: "5.5rem", cell: (j) => (ACTIVE.includes(j.state)
            ? <button className="btn-ghost h-8" onClick={(e) => { e.stopPropagation(); stop.ask(j); }}><Square className="h-4 w-4" />{t("Stop")}</button> : null) },
        ]} />
      {stop.sheet}
    </>
  );
}

/** S-03-2 (20/10 §3): step bar, health tiles, Recovery point, attempts table (where it resumes from), multi-source timeline. */
export function JobPage({ ws, id, onBack }: { ws: string; id: string; onBack: () => void }) {
  const qc = useQueryClient();
  const job = useQuery({ queryKey: ["job", ws, id], queryFn: () => api.scheduler.getJob({ workspaceId: ws, jobId: id }), refetchInterval: 2000 });
  useCrumb(id, job.data?.name);
  const ops = useQuery({ queryKey: ["job-ops", ws, id], queryFn: () => api.scheduler.getJobOps({ workspaceId: ws, jobId: id }), refetchInterval: 2000 });
  const resume = useMutation({ mutationFn: () => api.scheduler.resumeJob({ workspaceId: ws, jobId: id }), onSuccess: () => qc.invalidateQueries({ queryKey: ["job", ws, id] }) });
  const move = useMutation({ mutationFn: () => api.scheduler.moveAttemptNow({ workspaceId: ws, jobId: id }), onSuccess: () => qc.invalidateQueries({ queryKey: ["job-ops", ws, id] }) });
  const extend = useMutation({ mutationFn: () => api.scheduler.extendGrace({ workspaceId: ws, jobId: id, minutes: 15 }), onSuccess: () => qc.invalidateQueries({ queryKey: ["job-ops", ws, id] }) });
  const lines = useJobLog(ws, id);
  const stop = useCancelJob(ws);
  const now = useNow();
  const j = job.data, o = ops.data;
  if (!j) return <div className="p-6 text-slate-500">{job.error ? errorText(job.error) : "Loading…"}</div>;
  const p = o?.progress;
  const live = ["starting", "running", "scheduling"].includes(j.state);
  const attempt = o?.attempts[o.attempts.length - 1];
  const beatAge = attempt?.lastHeartbeatAt ? now - secs(attempt.lastHeartbeatAt) : null;
  const signalLost = live && attempt?.state === "running" && beatAge !== null && beatAge > 120;
  const durable = o?.checkpoints.filter((c) => c.location === "durable") ?? [];
  const lastMetric = o?.metrics[o.metrics.length - 1];
  const local = o?.checkpoints.filter((c) => c.location === "local") ?? [];
  const lastDurable = durable[0], lastLocal = local[0];
  return (
    <>
      <button className="mb-3 text-sm text-brand-500" onClick={onBack}>← Jobs</button>
      <PageHeader title={j.name} subtitle={<span className="flex flex-wrap items-center gap-2"><StateBadge state={j.state} />{j.kind} · {j.attemptCount} attempt{j.stateReason ? ` · ${reasonText(j.stateReason)}` : ""}</span>}
        actions={<>
          {ACTIVE.includes(j.state) && <button className="btn-ghost" onClick={() => stop.ask(j)}><Square className="h-4 w-4" />{t("Stop")}</button>}
          {j.state === "failed" && (o?.checkpoints.length ?? 0) > 0 && <button className="btn-primary" disabled={resume.isPending} onClick={() => resume.mutate()}>{t("Resume from checkpoint")}</button>}
        </>} />
      {stop.sheet}
      <ErrorBox error={resume.error ? errorText(resume.error) : null} />
      {attempt?.state === "suspect" && attempt.suspectUntil && (
        <div className="mb-4 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-800">
          {t("The machine running this job lost connection — waiting {d} more for it to return; after that the job resumes on another machine from the latest durable checkpoint.", { d: dur(secs(attempt.suspectUntil) - now) })}
          <span className="ml-2 space-x-2">
            <button className="btn-ghost py-0.5 text-xs" disabled={move.isPending} onClick={() => move.mutate()}>{t("Move to another machine now")}</button>
            <button className="btn-ghost py-0.5 text-xs" disabled={extend.isPending} onClick={() => extend.mutate()}>{t("Wait 15 more minutes")}</button>
          </span>
        </div>)}

      <div className="card mb-4">
        <PhaseBar state={j.state} progress={p} />
        {p && live && !(p.phase === "preparing_data" && p.unit === "B") && <div className="mt-3"><ProgressLine progress={p} /></div>}
        <div className={`mt-3 text-xs ${signalLost ? "text-rose-600" : "text-slate-500"}`}>
          {attempt ? <>{t("Heartbeat")} {beatAge !== null ? t("{n}s ago", { n: Math.max(0, Math.round(beatAge)) }) : "—"}{signalLost ? " · " + t("signal lost") : ""} · {t("progress updated")} {p?.at ? t("{n}s ago", { n: Math.max(0, Math.round(now - secs(p.at))) }) : "—"}</> : t("No attempt yet")}
        </div>
      </div>

      <DataSyncPanel ws={ws} jobId={id} progress={p} live={live} />

      <div className="mb-4 grid gap-4 md:grid-cols-2">
        <div className="card text-sm">
          <div className="mb-2 font-medium">{t("Recovery points")}</div>
          <div className="space-y-1 text-xs text-slate-600">
            <div>{t("Durable checkpoint")}: {lastDurable ? <>{t("step {n}", { n: String(lastDurable.step) })} · {(Number(lastDurable.sizeBytes) / 1024).toFixed(1)} KB · {ago(lastDurable.createdAt!)}</> : t("none yet")}</div>
            <div>{t("Local checkpoint")}: {lastLocal ? <>{t("step {n}", { n: String(lastLocal.step) })} · {ago(lastLocal.createdAt!)}</> : t("none yet")}</div>
          </div>
        </div>
        <div className="card text-sm">
          <div className="mb-2 font-medium">Attempt</div>
          <table className="w-full text-xs">
            <thead className="text-left text-slate-500"><tr><th>#</th><th>{t("Status")}</th><th>{t("Reason")}</th><th>{t("Resume from")}</th></tr></thead>
            <tbody>{(o?.attempts ?? []).map((a) => (
              <tr key={a.id} className="border-t border-slate-100"><td className="py-1">{a.number}</td><td><StateBadge state={a.state} /></td>
                <td className="text-slate-500">{a.exitReason}{a.exitCode ? ` (${t("code")} ${a.exitCode})` : ""}</td><td>{a.resumedFrom || "—"}</td></tr>))}</tbody>
          </table>
        </div>
      </div>

      {lastMetric && (
        <div className="card mb-4 text-xs text-slate-600">
          Container: CPU {lastMetric.cpuPercent.toFixed(0)}% · RAM {(Number(lastMetric.memoryBytes) / 1048576).toFixed(0)} MB
          {lastMetric.gpuUtil > 0 && ` · GPU ${lastMetric.gpuUtil.toFixed(0)}% · VRAM ${(Number(lastMetric.vramBytes) / 1048576).toFixed(0)} MB`} · {lastMetric.at ? ago(lastMetric.at) : ""}
        </div>)}
      <div className="mb-4"><LogPanel lines={lines} name={j.name} /></div>
      <div className="card">
        <div className="mb-2 text-sm font-medium">{t("Timeline")}</div>
        <EventList events={o?.events ?? []} />
      </div>
    </>
  );
}
