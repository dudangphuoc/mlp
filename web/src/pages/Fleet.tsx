import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { OsFamily } from "../gen/mlplatform/v1/agent_pb";
import type { AgentUpgrade, Host } from "../gen/mlplatform/v1/fleet_pb";
import { bytes } from "../lib/format";
import {
  CommandBlock, Dialog, EmptyState, ErrorBox, Field, ProgressSteps, RelTime, Skeleton, StateBadge,
} from "../components/ui";
import { ClusterMapView } from "./Ops";
import { DataCacheList } from "../components/DataSync";
import { RayHealthView } from "../components/RayHealth";
import { translate as t } from "../i18n";
import { GpuPanel } from "../components/hostops/GpuPanel";
import { TerminalPanel } from "../components/hostops/TerminalPanel";
import { FileBrowser } from "../components/hostops/FileBrowser";
import { FileEditor } from "../components/hostops/FileEditor";
import { ProcessesPanel } from "../components/hostops/ProcessesPanel";
import { ServicesPanel } from "../components/hostops/ServicesPanel";
import { ChevronLeft, Plus, X, CheckCircle2, CircleX, Clock, Loader2, RefreshCw } from "lucide-react";

type View = "map" | "ray" | "data" | "agents";
type LeftTab = "files" | "processes" | "services" | "gpu" | "activity";
type MobileTab = "files" | "terminal" | "processes" | "services";
type PaneTab = { id: number; kind: "terminal" } | { id: number; kind: "file"; path: string };
const search = () => new URLSearchParams(window.location.search);
const online = (h: Host) => h.state === "online" || h.state === "busy";
const seg = (on: boolean) => `inline-flex h-8 flex-none items-center justify-center whitespace-nowrap rounded-md px-3 text-sm ${on ? "bg-surface-2 text-fg" : "text-fg-muted hover:text-fg"}`;

/** Machines (mockup A): machine list, one screen per machine — compact GPU strip, files and tools left, terminals right. */
export function FleetPage({ ws, go }: { ws: string; go: (p: string) => void }) {
  const qc = useQueryClient();
  const q = search();
  const view = q.get("tab") as View | null;
  const hosts = useQuery({ queryKey: ["hosts", ws], queryFn: () => api.fleet.listHosts({ workspaceId: ws }), refetchInterval: 5000 });
  const upgrades = useQuery({ queryKey: ["agent-upgrades", ws], queryFn: () => api.fleet.listAgentUpgrades({ workspaceId: ws }), refetchInterval: 3000 });
  const upgrade = useMutation({
    mutationFn: (v: { hostIds: string[]; force?: boolean }) => api.fleet.upgradeAgents({ workspaceId: ws, hostIds: v.hostIds, force: v.force ?? false }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["agent-upgrades", ws] }),
  });
  const list = hosts.data?.hosts ?? [];
  const hostParam = q.get("host");
  const selected = list.find((h) => h.id === hostParam) ?? list[0];
  const running = new Set((upgrades.data?.upgrades ?? []).filter((u) => u.state === "queued" || u.state === "running").map((u) => u.hostId));
  const nav = <MachineNav ws={ws} hosts={list} selectedId={selected?.id} view={view} go={go} />;
  return (
    <>
      <ErrorBox error={hosts.error ? errorText(hosts.error) : upgrade.error ? errorText(upgrade.error) : null} onRetry={() => void hosts.refetch()} />
      <div className="overflow-hidden rounded-lg border border-line bg-surface lg:grid lg:h-[calc(100dvh-9.5rem)] lg:grid-cols-[208px_minmax(0,1fr)]">
        <div className={`${hostParam || view ? "hidden lg:block" : ""} min-h-0 border-line lg:border-r`}>{nav}</div>
        <div className={`${hostParam || view ? "" : "hidden lg:block"} min-h-0 min-w-0`}>
          {view ? <div className="h-full overflow-auto p-4">
              {view === "map" ? <ClusterMapView ws={ws} go={go} /> : view === "data" ? <DataCacheList ws={ws} go={go} /> : view === "ray" ? <RayHealthView ws={ws} />
                : <UpgradeHistory items={upgrades.data?.upgrades ?? []} latest={list[0]?.latestAgentVersion} loading={upgrades.isLoading} />}</div>
            : hosts.isLoading ? <Skeleton rows={6} />
            : !selected ? <EmptyState title={t("No machines yet")} action={<button className="btn-primary" onClick={() => go("fleet?new=1")}>{t("Add your first machine")}</button>} />
            : <MachineScreen key={selected.id} ws={ws} h={selected} busy={running.has(selected.id)} go={go}
                onUpgrade={(force) => upgrade.mutate({ hostIds: [selected.id], force })} />}
        </div>
      </div>
      {q.get("new") === "1" && <EnrollDialog ws={ws} onClose={() => go("fleet")} />}
    </>
  );
}

function MachineNav({ ws, hosts, selectedId, view, go }: { ws: string; hosts: Host[]; selectedId?: string; view: View | null; go: (p: string) => void }) {
  const links: [View, string][] = [["map", "Cluster map"], ["ray", "Ray"], ["data", "Data on machines"], ["agents", "Agent updates"]];
  return (
    <nav className="flex h-full min-h-0 flex-col">
      <div className="flex h-12 flex-none items-center border-b border-line px-4 font-semibold">{t("Machines")}</div>
      <div className="min-h-0 flex-1 space-y-1 overflow-y-auto p-2">
        {hosts.map((h) => <MachineRow key={h.id} ws={ws} h={h} active={!view && h.id === selectedId} onClick={() => go(`fleet?host=${h.id}`)} />)}
      </div>
      <div className="flex-none space-y-1 border-t border-line p-2">
        {links.map(([k, label]) => (
          <button key={k} className={`flex h-8 w-full items-center whitespace-nowrap rounded-md px-3 text-sm ${view === k ? "bg-surface-2 text-fg" : "text-fg-muted hover:text-fg"}`}
            onClick={() => go(`fleet?tab=${k}`)}>{t(label)}</button>))}
        <button className="flex h-9 w-full items-center justify-center whitespace-nowrap rounded-md border border-line text-sm hover:bg-surface-2" onClick={() => go("fleet?new=1")}>{t("+ Add machine")}</button>
      </div>
    </nav>
  );
}

function useGpus(ws: string, h: Host) {
  return useQuery({ queryKey: ["host-gpus", ws, h.id], queryFn: () => api.fleet.getHostGpus({ workspaceId: ws, hostId: h.id }),
    refetchInterval: 15000, enabled: (h.hostInfo?.gpus.length ?? 0) > 0 && online(h) });
}

function MachineRow({ ws, h, active, onClick }: { ws: string; h: Host; active: boolean; onClick: () => void }) {
  const g = useGpus(ws, h).data?.gpus[0]?.sample;
  const sub = !online(h) ? t(h.state) : g ? `GPU ${g.utilPercent.toFixed(0)}% · ${g.temperatureC.toFixed(0)}°C` : `${h.hostInfo?.logicalCpus ?? "—"} CPU`;
  return (
    <button onClick={onClick} className={`grid h-[52px] w-full grid-cols-[8px_minmax(0,1fr)] items-center gap-x-2 rounded-md px-2.5 text-left ${active ? "bg-surface-2" : "hover:bg-surface-2/60"}`}>
      <span className={`h-2 w-2 rounded-full ${online(h) ? "bg-success" : h.state === "degraded" ? "bg-warning" : "bg-fg-subtle"}`} />
      <span className="min-w-0"><span className="block truncate text-sm font-medium">{h.name}</span><span className="block truncate text-xs text-fg-muted">{sub}</span></span>
    </button>
  );
}

function Meter({ label, pct, value, tone, width }: { label: string; pct: number; value: string; tone: string; width: string }) {
  return (
    <span className="grid flex-none grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-x-2" style={{ width }}>
      <span className="whitespace-nowrap text-fg-muted">{label}</span>
      <span className="h-1.5 rounded-full bg-surface-2"><span className={`block h-full rounded-full ${tone}`} style={{ width: `${Math.max(0, Math.min(100, pct))}%` }} /></span>
      <span className="whitespace-nowrap text-right tabular-nums">{value}</span>
    </span>
  );
}

function GpuStrip({ ws, h }: { ws: string; h: Host }) {
  const v = useGpus(ws, h).data;
  if (!v || v.gpus.length === 0) return null;
  return (
    <div className="flex-none border-b border-line">
      {v.gpus.map((c) => {
        const s = c.sample!;
        const total = Number(s.memoryTotalBytes || c.info?.memoryBytes || 1n);
        const used = Number(s.memoryUsedBytes);
        return (
          <div key={c.info?.uuid} className="flex h-10 items-center gap-5 overflow-x-auto px-4 text-xs">
            <span className="flex-none whitespace-nowrap font-medium">GPU {c.info?.index} · {c.info?.name}</span>
            <Meter label={t("Util")} pct={s.utilPercent} value={`${s.utilPercent.toFixed(0)}%`} tone="bg-accent" width="180px" />
            <Meter label="VRAM" pct={(used / total) * 100} value={`${bytes(used)} / ${bytes(total)}`} tone="bg-violet-400" width="240px" />
            <span className="flex-none whitespace-nowrap"><span className="text-fg-muted">{t("Temperature")} </span>{s.temperatureC.toFixed(0)}°C</span>
            <span className="flex-none whitespace-nowrap"><span className="text-fg-muted">{t("Power")} </span>{s.powerW.toFixed(0)}{s.powerLimitW !== undefined ? ` / ${s.powerLimitW.toFixed(0)}` : ""} W</span>
            <span className="flex-none whitespace-nowrap"><span className="text-fg-muted">{t("Free to schedule")} </span>{bytes(c.schedulableMemoryBytes)}</span>
            {c.alerts.map((a) => <span key={a.kind} className={`badge flex-none ${a.level === "crit" ? "bg-danger/15 text-danger" : "bg-warning/15 text-warning"}`}>{t(a.kind)}</span>)}
            {v.yield?.active && <span className="badge flex-none bg-warning/15 text-warning">{t("yielded")}</span>}
          </div>);
      })}
    </div>
  );
}

function MachineHeader({ h, busy, onUpgrade, back }: { h: Host; busy: boolean; onUpgrade: (force: boolean) => void; back?: () => void }) {
  const i = h.hostInfo;
  return (
    <header className="flex h-12 flex-none items-center gap-3 border-b border-line px-2 lg:px-4">
      {back && <button className="inline-flex h-10 w-10 flex-none items-center justify-center lg:hidden" aria-label={t("Machines")} onClick={back}><ChevronLeft className="h-5 w-5" /></button>}
      <span className="min-w-0 truncate text-base font-semibold">{h.name}</span>
      <StateBadge state={h.state} reason={h.stateReason || undefined} />
      <span className="hidden min-w-0 truncate text-sm text-fg-muted xl:block">
        {[i?.osDescription, i ? `${i.logicalCpus} CPU` : "", i ? `${bytes(i.memoryBytes)} RAM` : "", i?.dockerVersion ? `Docker ${i.dockerVersion}` : "", h.overlayIp ? `VPN ${h.overlayIp}` : ""].filter(Boolean).join(" · ")}
      </span>
      <span className="ml-auto hidden flex-none whitespace-nowrap text-xs text-fg-muted lg:block">agent {h.agentVersion.slice(0, 7)}</span>
      {busy ? <span className="badge flex-none bg-sky-100 text-sky-700">{t("updating…")}</span>
        : h.updateAvailable ? <button className="btn-primary h-8 flex-none whitespace-nowrap px-3" onClick={() => onUpgrade(false)}><RefreshCw className="h-4 w-4" />{t("Update agent")}</button>
        : null}
    </header>
  );
}

function MachineScreen({ ws, h, busy, go, onUpgrade }: { ws: string; h: Host; busy: boolean; go: (p: string) => void; onUpgrade: (force: boolean) => void }) {
  const [left, setLeft] = useState<LeftTab>("files");
  const [mobile, setMobile] = useState<MobileTab>("files");
  const [tabs, setTabs] = useState<PaneTab[]>([{ id: 1, kind: "terminal" }]);
  const [active, setActive] = useState(1);
  const live = online(h);
  const openFile = (path: string) => {
    const id = Date.now();
    setTabs((ts) => [...ts, { id, kind: "file", path }]);
    setActive(id);
    setMobile("terminal");
  };
  const addTerminal = () => { const id = Date.now(); setTabs((ts) => [...ts, { id, kind: "terminal" }]); setActive(id); };
  const close = (id: number) => setTabs((ts) => { const rest = ts.filter((x) => x.id !== id); if (active === id && rest.length) setActive(rest[rest.length - 1].id); return rest; });
  const leftBody = (tab: LeftTab, compact: boolean) =>
    tab === "activity" ? <div className="p-3"><HostActivityPanel ws={ws} h={h} /></div>
    : !live ? <EmptyState bare title={t("The machine is offline.")} />
    : tab === "files" ? <FileBrowser ws={ws} hostId={h.id} onOpenFile={openFile} compact={compact} />
    : tab === "processes" ? <ProcessesPanel ws={ws} hostId={h.id} />
    : tab === "services" ? <ServicesPanel ws={ws} hostId={h.id} />
    : <div className="p-3"><GpuPanel ws={ws} hostId={h.id} /></div>;
  const pane = <WorkPane ws={ws} hostId={h.id} live={live} tabs={tabs} active={active} setActive={setActive} add={addTerminal} close={close} />;
  const leftTabs: [LeftTab, string][] = [["files", "Files"], ["processes", "Processes"], ["services", "Services"], ["gpu", "GPU history"], ["activity", "Activity"]];
  return (
    <div className="flex h-full min-h-0 flex-col">
      <MachineHeader h={h} busy={busy} onUpgrade={onUpgrade} back={() => go("fleet")} />
      <GpuStrip ws={ws} h={h} />
      <div className="hidden min-h-0 flex-1 grid-cols-[minmax(0,1fr)_minmax(0,1fr)] lg:grid">
        <section className="flex min-h-0 flex-col border-r border-line">
          <div className="flex h-10 flex-none items-center gap-1 overflow-x-auto border-b border-line px-2">
            {leftTabs.map(([k, label]) => <button key={k} className={seg(left === k)} onClick={() => setLeft(k)}>{t(label)}</button>)}
          </div>
          <div className="min-h-0 flex-1 overflow-auto">{leftBody(left, false)}</div>
        </section>
        {pane}
      </div>
      <div className="flex min-h-[calc(100dvh-12rem)] flex-1 flex-col lg:hidden">
        <div className="grid h-12 flex-none grid-cols-4 items-center gap-1 border-b border-line px-2">
          {(["files", "terminal", "processes", "services"] as MobileTab[]).map((k) => (
            <button key={k} className={seg(mobile === k)} onClick={() => setMobile(k)}>{t(k === "files" ? "Files" : k === "terminal" ? "Terminal" : k === "processes" ? "Processes" : "Services")}</button>))}
        </div>
        <div className="flex min-h-0 flex-1 flex-col">{mobile === "terminal" ? pane : leftBody(mobile, true)}</div>
      </div>
    </div>
  );
}

function WorkPane({ ws, hostId, live, tabs, active, setActive, add, close }:
  { ws: string; hostId: string; live: boolean; tabs: PaneTab[]; active: number; setActive: (id: number) => void; add: () => void; close: (id: number) => void }) {
  let n = 0;
  return (
    <section className="flex min-h-0 flex-1 flex-col bg-[#0b0e13]">
      <div className="flex h-10 flex-none items-center gap-1 overflow-x-auto border-b border-line px-2">
        {tabs.map((tb) => {
          const label = tb.kind === "terminal" ? `${t("Terminal")} ${++n}` : tb.path.split(/[\\/]/).pop() ?? tb.path;
          return (
            <span key={tb.id} className={`inline-flex h-8 flex-none items-center rounded-md ${active === tb.id ? "bg-surface-2 text-fg" : "text-fg-muted hover:text-fg"}`}>
              <button className="h-8 whitespace-nowrap pl-3 pr-1 text-sm" onClick={() => setActive(tb.id)}>{label}</button>
              <button className="inline-flex h-8 w-7 items-center justify-center" aria-label={t("Close")} onClick={() => close(tb.id)}><X className="h-3.5 w-3.5" /></button>
            </span>);
        })}
        <button className="inline-flex h-8 w-8 flex-none items-center justify-center rounded-md text-fg-muted hover:text-fg" aria-label={t("New terminal")} onClick={add}><Plus className="h-4 w-4" /></button>
      </div>
      <div className="relative min-h-0 flex-1">
        {!live ? <EmptyState bare title={t("The machine is offline.")} /> : tabs.map((tb) => (
          <div key={tb.id} className={`absolute inset-0 flex flex-col ${active === tb.id ? "" : "invisible"}`}>
            {tb.kind === "terminal" ? <TerminalPanel ws={ws} hostId={hostId} /> : <FileEditor ws={ws} hostId={hostId} path={tb.path} />}
          </div>))}
      </div>
    </section>
  );
}

const PHASES = ["building", "downloading", "checking", "stopping", "installing", "starting", "connecting", "done"] as const;
const PHASE_LABEL: Record<string, string> = {
  building: "Build agent for the platform", downloading: "Download new build (signed link)", checking: "Verify SHA-256", stopping: "Stop old agent",
  installing: "Replace binary", starting: "Start new agent", connecting: "Wait for a stable connection ≥ 30 s", done: "Done",
};

function UpgradeHistory({ items, latest, loading }: { items: AgentUpgrade[]; latest?: string; loading: boolean }) {
  if (loading) return <Skeleton rows={4} />;
  if (items.length === 0) return (
    <EmptyState title={t("No agent updates yet")}>
      {t("Current agent version:")} <span className="mono">{latest?.slice(0, 10) || "—"}</span>
    </EmptyState>);
  return (
    <div className="space-y-3">
      {items.map((u) => {
        const idx = PHASES.indexOf(u.phase as (typeof PHASES)[number]);
        const failed = u.state === "failed";
        return (
          <div key={u.id} className="card">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                <span className="font-medium">{u.hostName}</span>
                <span className="mono text-xs text-fg-subtle">{u.fromVersion.slice(0, 10) || "?"} → {u.toVersion.slice(0, 10)}</span>
              </div>
              <div className="flex items-center gap-3 text-xs text-fg-muted"><RelTime at={u.createdAt} /><StateBadge state={u.state} /></div>
            </div>
            {u.state === "skipped" ? <div className="text-sm text-fg-muted">{t(u.message)}</div> : (
              <ProgressSteps steps={PHASES.map((p, i) => ({
                label: t(PHASE_LABEL[p]),
                state: u.state === "succeeded" || i < idx ? "done" : i === idx ? (failed ? "failed" : "active") : "pending",
              }))} />)}
            {failed && <div className="mt-3 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700">{t(u.message)}</div>}
          </div>);
      })}
    </div>
  );
}

function EnrollDialog({ ws, onClose }: { ws: string; onClose: () => void }) {
  const [os, setOs] = useState(OsFamily.LINUX);
  const [dataPath, setData] = useState("/datasets");
  const [modelsPath, setModels] = useState("/models");
  const m = useMutation({ mutationFn: () => api.fleet.createEnrollment({ workspaceId: ws, os, dataPath, modelsPath }) });
  const pick = (v: OsFamily) => {
    setOs(v);
    if (v === OsFamily.WINDOWS) { setData("C:\\mlp\\datasets"); setModels("C:\\mlp\\models"); } else { setData("/datasets"); setModels("/models"); }
  };
  return (
    <Dialog title={t("Add a new machine")} onClose={onClose} wide>
      {!m.data ? (
        <div className="space-y-4">
          <Field label={t("Operating system")}>
            <div className="flex gap-2">{([[OsFamily.LINUX, "Linux"], [OsFamily.WINDOWS, "Windows"], [OsFamily.MACOS, "macOS"]] as const).map(([v, l]) => (
              <button key={l} type="button" className={os === v ? "btn-primary" : "btn-ghost"} onClick={() => pick(v)}>{l}</button>))}</div>
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label={t("Data folder")}><input className="input" value={dataPath} onChange={(e) => setData(e.target.value)} /></Field>
            <Field label={t("Models folder")}><input className="input" value={modelsPath} onChange={(e) => setModels(e.target.value)} /></Field>
          </div>
          <ErrorBox error={m.error ? errorText(m.error) : null} />
          <div className="flex justify-end"><button className="btn-primary" onClick={() => m.mutate()} disabled={m.isPending}>{t("Create install command")}</button></div>
        </div>
      ) : (
        <div className="space-y-4">
          <CommandBlock text={m.data.installCommand} secret={m.data.token} />
          <ProgressSteps steps={[
            { label: t("Doctor checks the machine (Docker, GPU, disk, network)"), state: "active" },
            { label: t("Control plane builds the agent for this platform (cached for next machines)"), state: "pending" },
            { label: t("Install service + TLS with pinned certificate"), state: "pending" },
            { label: t("Overlay network + Ray node"), state: "pending" },
          ]} />
          <div className="flex justify-end"><button className="btn-ghost" onClick={onClose}>Xong</button></div>
        </div>
      )}
    </Dialog>
  );
}

const TRACE: Record<string, { icon: typeof CheckCircle2; cls: string; label: string }> = {
  running: { icon: Loader2, cls: "text-info", label: "running" },
  ok: { icon: CheckCircle2, cls: "text-success", label: "done" },
  failed: { icon: CircleX, cls: "text-danger", label: "failed" },
  timeout: { icon: Clock, cls: "text-warning", label: "no answer" },
  lost: { icon: CircleX, cls: "text-danger", label: "connection lost" },
};

/** What the control plane asked this machine to do, and the agent's own log — so nothing happens on a machine unseen. */
function HostActivityPanel({ ws, h }: { ws: string; h: Host }) {
  const q = useQuery({ queryKey: ["host-activity", ws, h.id], queryFn: () => api.fleet.getHostActivity({ workspaceId: ws, hostId: h.id }), refetchInterval: 4000 });
  const a = q.data;
  return (
    <div>
      <ErrorBox error={q.error ? errorText(q.error) : null} />
      {!a ? <Skeleton rows={6} /> : (
        <div className="space-y-5">
          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wider text-fg-subtle">{t("Commands sent")}</h3>
            {a.commands.length === 0 ? <p className="text-sm text-fg-muted">{a.online ? t("No command since the agent connected.") : t("The machine is offline.")}</p> : (
              <ol className="space-y-1.5">
                {a.commands.map((c) => {
                  const st = TRACE[c.state] ?? TRACE.failed;
                  return (
                    <li key={c.id} className="flex items-start gap-2 text-sm">
                      <st.icon className={`mt-0.5 h-4 w-4 shrink-0 ${st.cls} ${c.state === "running" ? "animate-spin" : ""}`} />
                      <span className="min-w-0 flex-1">
                        <span className="mono break-all text-xs">{c.detail}</span>
                        <span className="block text-xs text-fg-muted">{t(st.label)} · {t("started")} <RelTime at={c.startedAt} />
                          {c.finishedAt ? <> · {t("finished")} <RelTime at={c.finishedAt} /></> : <> · {t("waits up to {m} min", { m: Math.round(c.limitSeconds / 60) })}</>}</span>
                        {c.message && <span className="block break-words text-xs text-danger">{t(c.message)}</span>}
                      </span>
                    </li>);
                })}
              </ol>)}
          </section>
          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wider text-fg-subtle">{t("Agent log (latest lines)")}</h3>
            {!a.logError && (() => {
              const last = a.agentLog.trim().split("\n").pop() ?? "";
              const at = Date.parse(last.slice(0, 20));
              return Number.isFinite(at) && Date.now() - at > 10 * 60_000
                ? <p className="mb-2 rounded-md border border-warning/30 bg-warning/10 px-2 py-1 text-xs text-warning">
                    {t("The newest log line is from {when}: this agent version does not write its log. Update the agent to see what it does.", { when: new Date(at).toLocaleString() })}</p>
                : null;
            })()}
            {a.logError ? <p className="text-sm text-warning">{t(a.logError)}</p>
              : <pre className="mono max-h-[50vh] overflow-auto whitespace-pre-wrap break-all rounded-lg border border-line bg-[#0b0e13] p-3 text-[11px] leading-5" style={{ color: "#e6e9ef" }}>
                  {a.agentLog.split("\n").slice(-200).join("\n")}</pre>}
          </section>
        </div>)}
    </div>
  );
}
