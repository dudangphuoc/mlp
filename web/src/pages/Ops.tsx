import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ago, bytes } from "../lib/format";
import { EmptyState, ErrorBox, PageHeader, StateBadge } from "../components/ui";
import type { ActiveJob, SystemStatus, WorkspaceUpdate } from "../gen/mlplatform/v1/ops_pb";
import type { LogLine, OpsEvent } from "../gen/mlplatform/v1/scheduler_pb";
import { translate as t } from "../i18n";
import { TriangleAlert, X } from "lucide-react";

type Live = { jobs: ActiveJob[]; system?: SystemStatus; subscribe: (jobId: string | null) => void; events: OpsEvent[]; logs: LogLine[] };
const LiveCtx = createContext<Live>({ jobs: [], subscribe: () => {}, events: [], logs: [] });
export const useLive = () => useContext(LiveCtx);

/**
 * One stream per tab (20/10 §2.7 `WatchWorkspace`): running work, system status, and events + logs of the open job.
 * Changing the subscribed job → reopen the stream; on disconnect → reconnect after 3 s (the server sends a full snapshot first).
 */
export function LiveProvider({ ws, children }: { ws: string; children: ReactNode }) {
  const [jobs, setJobs] = useState<ActiveJob[]>([]);
  const [system, setSystem] = useState<SystemStatus>();
  const [sub, setSub] = useState<string | null>(null);
  const [events, setEvents] = useState<OpsEvent[]>([]);
  const [logs, setLogs] = useState<LogLine[]>([]);
  useEffect(() => {
    setEvents([]); setLogs([]);
    const ctrl = new AbortController();
    let stopped = false;
    (async () => {
      while (!stopped) {
        try {
          for await (const u of api.ops.watchWorkspace({ workspaceId: ws, subscribeJobId: sub ?? "" }, { signal: ctrl.signal }) as AsyncIterable<WorkspaceUpdate>) {
            if (u.jobsChanged) setJobs(u.jobs);
            if (u.system) setSystem(u.system);
            if (u.events.length) setEvents((e) => { const seen = new Set(e.map((x) => x.id)); return [...e, ...u.events.filter((x) => !seen.has(x.id))]; });
            if (u.logs.length) setLogs((l) => [...l, ...u.logs].slice(-3000));
          }
        } catch { /* reconnect */ }
        if (!stopped) await new Promise((r) => setTimeout(r, 3000));
      }
    })();
    return () => { stopped = true; ctrl.abort(); };
  }, [ws, sub]);
  return <LiveCtx.Provider value={{ jobs, system, subscribe: setSub, events, logs }}>{children}</LiveCtx.Provider>;
}

/** S-00-3 "Running" tray: all my work — step, %, ETA; red dot on error/stall/signal loss/waiting for a machine. */
export function RunningTray({ go }: { go: (p: string) => void }) {
  const { jobs } = useLive();
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const active = jobs.filter((j) => ["queued", "scheduling", "starting", "running"].includes(j.state));
  const alert = jobs.some((j) => j.alert);
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);
  return (
    <div ref={box} className="relative">
      <button className="flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-sm text-slate-600 hover:bg-slate-100" onClick={() => setOpen(!open)}
        aria-label={t("{n} running", { n: active.length })}>
        <span aria-hidden>⏱</span> {t("Running")}
        <span className={`ml-auto rounded-full px-1.5 text-[11px] font-semibold ${alert ? "bg-rose-500 text-white" : active.length ? "bg-sky-100 text-sky-700" : "text-slate-400"}`}>{active.length}</span>
      </button>
      {open && (
        <div className="absolute bottom-10 left-0 z-50 w-96 rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
          {jobs.length === 0 && <div className="p-4 text-center text-sm text-slate-400">{t("Nothing is running.")}</div>}
          {jobs.map((j) => (
            <button key={j.id} className="block w-full rounded-lg px-2 py-2 text-left hover:bg-slate-50" onClick={() => { setOpen(false); go(`jobs/${j.id}`); }}>
              <div className="flex items-center gap-2 text-sm">
                {j.alert && <span className="h-2 w-2 rounded-full bg-rose-500" title={t(j.alertText)} />}
                <span className="min-w-0 flex-1 truncate font-medium">{j.name}</span><StateBadge state={j.state} />
              </div>
              <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100"><div className="h-full bg-sky-500" style={{ width: `${j.state === "succeeded" ? 100 : j.percent}%` }} /></div>
              <div className="mt-0.5 text-xs text-slate-500">
                {j.alert ? <span className="text-rose-600">{t(j.alertText)}</span> : j.phase || j.stateReason}
                {j.percent > 0 && ` · ${j.percent.toFixed(0)}%`}{j.etaSeconds > 0 && ` · ${t("~{n} min left", { n: Math.ceil(j.etaSeconds / 60) })}`}
              </div>
            </button>))}
        </div>)}
    </div>
  );
}

/** Global incident banner (20/10 §2.6): tells users the consequences clearly. */
/** Incident banner: one line, dismissible (remembered per incident set — reappears when a new incident occurs). */
export function IncidentBanner({ go }: { go: (p: string) => void }) {
  const { system } = useLive();
  const down = system?.items.filter((i) => i.status !== "ok") ?? [];
  const sig = down.map((d) => d.name).sort().join("|");
  const [dismissed, setDismissed] = useState(() => { try { return localStorage.getItem("mlp.incident.dismissed") ?? ""; } catch { return ""; } });
  if (down.length === 0 || dismissed === sig) return null;
  const close = () => { try { localStorage.setItem("mlp.incident.dismissed", sig); } catch { /* ignore */ } setDismissed(sig); };
  return (
    <div className="mb-4 flex items-center gap-3 rounded-lg border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
      <TriangleAlert className="h-4 w-4 shrink-0" />
      <button className="min-w-0 flex-1 truncate text-left" onClick={() => go("status")} title={down.map((d) => `${d.name}: ${t(d.message)}`).join("\n")}>
        <span className="font-medium">{t("{n} incidents", { n: down.length })}:</span> {down.map((d) => t(d.name)).join(", ")}
        <span className="ml-2 underline">{t("view system status")}</span>
      </button>
      <button className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md hover:bg-danger/15" onClick={close} aria-label={t("Hide alert")}><X className="h-4 w-4" /></button>
    </div>
  );
}

const tone: Record<string, string> = { ok: "bg-emerald-500", degraded: "bg-amber-400", down: "bg-rose-500" };

/** S-OPS-3 System status: per-component lights + background loops, 30-day incidents. */
export function StatusPage({ ws }: { ws: string }) {
  const s = useQuery({ queryKey: ["status", ws], queryFn: () => api.ops.getSystemStatus({ workspaceId: ws }), refetchInterval: 10000 });
  const items = s.data?.items ?? [];
  return (
    <>
      <PageHeader title={t("System status")} subtitle={s.data ? (s.data.overall === "ok" ? t("All components are operating normally") : t("Some components are having problems")) : ""} />
      <ErrorBox error={s.error ? errorText(s.error) : null} />
      <div className="mb-4 grid gap-4 md:grid-cols-2">
        {[["component", "Components"], ["loop", "Background loops"]].map(([g, label]) => (
          <div key={g} className="card">
            <div className="mb-2 text-sm font-medium">{t(label)}</div>
            {items.filter((i) => i.group === g).map((i) => (
              <div key={t(i.name)} className="flex items-center gap-2 border-t border-slate-100 py-1.5 text-sm">
                <span className={`h-2.5 w-2.5 rounded-full ${tone[i.status] ?? "bg-slate-300"}`} />
                <span className="flex-1">{t(i.name)}</span>
                <span className="text-xs text-slate-500">{i.status === "ok" ? (i.checkedAt ? ago(i.checkedAt) : "") : t(i.message)}</span>
              </div>))}
          </div>))}
      </div>
      <div className="card">
        <div className="mb-2 text-sm font-medium">{t("Incidents (30 days)")}</div>
        {(s.data?.incidents ?? []).length === 0 ? <div className="text-sm text-slate-500">{t("No incidents.")}</div> :
          (s.data!.incidents).map((e) => (
            <div key={e.id} className="flex gap-2 border-t border-slate-100 py-1 text-xs">
              <span className={`mt-1 h-2 w-2 shrink-0 rounded-full ${e.severity === "error" ? "bg-rose-500" : e.severity === "success" ? "bg-emerald-500" : "bg-amber-400"}`} />
              <span className="w-28 shrink-0 text-slate-400">{e.at ? ago(e.at) : ""}</span><span>{t(e.message)}</span>
            </div>))}
      </div>
    </>
  );
}

/** S-02-5 Cluster map: one card per machine — which job holds each container and GPU, heartbeat, "recovering n jobs". */
export function ClusterMapView({ ws, go }: { ws: string; go: (p: string) => void }) {
  const m = useQuery({ queryKey: ["cluster-map", ws], queryFn: () => api.ops.getClusterMap({ workspaceId: ws }), refetchInterval: 5000 });
  const hosts = m.data?.hosts ?? [];
  if (hosts.length === 0) return <EmptyState title={t("No machines yet.")} />;
  return (
    <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 min-[2200px]:grid-cols-5">
      {hosts.map((h) => (
        <div key={h.id} className="card space-y-2 text-sm">
          <div className="flex items-center justify-between"><span className="font-medium">{h.name}</span><StateBadge state={h.state} /></div>
          <div className="text-xs text-slate-500">
            {t("heartbeat")} {h.lastSeenAt ? ago(h.lastSeenAt) : "—"} · {h.cpus} CPU · {h.memoryBytes ? bytes(BigInt(h.memoryBytes)) : "—"} RAM
            {h.recoveringJobs > 0 && <span className="ml-1 text-amber-600">· {t("recovering {n} jobs", { n: h.recoveringJobs })}</span>}
          </div>
          {h.gpus.length > 0 && (
            <div className="space-y-1">
              {h.gpus.map((g) => (
                <button key={g.uuid} className="block w-full rounded bg-slate-50 px-2 py-1 text-left text-xs hover:bg-slate-100" disabled={!g.jobId} onClick={() => go(`jobs/${g.jobId}`)}>
                  {g.name} · {g.utilPercent.toFixed(0)}% · {bytes(BigInt(g.memoryUsedBytes))}/{bytes(BigInt(g.memoryBytes))} — {g.jobName || <span className="text-slate-400">{t("free")}</span>}
                </button>))}
            </div>)}
          <div className="text-xs font-semibold text-slate-500">CONTAINER ({h.containers.length})</div>
          {h.containers.map((c) => (
            <button key={c.name} className="flex w-full items-center justify-between rounded px-1 py-0.5 text-left text-xs hover:bg-slate-50" disabled={!c.jobId} onClick={() => go(`jobs/${c.jobId}`)}>
              <span className="truncate">{c.jobName || c.name}</span><span className="text-slate-500">{c.cpuPercent.toFixed(0)}% · {bytes(BigInt(c.memoryBytes))}</span>
            </button>))}
          {h.metricsAt && <div className="text-[11px] text-slate-400">metric {ago(h.metricsAt)}</div>}
        </div>))}
    </div>
  );
}
