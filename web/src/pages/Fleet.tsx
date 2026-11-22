import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { OsFamily } from "../gen/mlplatform/v1/agent_pb";
import type { AgentUpgrade, Host } from "../gen/mlplatform/v1/fleet_pb";
import { bytes } from "../lib/format";
import {
  CommandBlock, Dialog, EmptyState, ErrorBox, Field, ProgressSteps, RelTime, Skeleton, StateBadge, Tabs,
} from "../components/ui";
import { ClusterMapView } from "./Ops";
import { DataCacheList } from "../components/DataSync";
import { RayHealthView } from "../components/RayHealth";
import { translate as t } from "../i18n";
import { GpuPanel } from "../components/hostops/GpuPanel";
import { TerminalPanel } from "../components/hostops/TerminalPanel";
import { FilesPanel } from "../components/hostops/FilesPanel";
import { ProcessesPanel } from "../components/hostops/ProcessesPanel";
import { ServicesPanel } from "../components/hostops/ServicesPanel";
import { CheckCircle2, CircleX, Clock, Loader2, RefreshCw } from "lucide-react";

type Tab = "machines" | "map" | "ray" | "data" | "agents";
type HostTab = "overview" | "gpu" | "terminal" | "files" | "processes" | "services" | "activity";
const search = () => new URLSearchParams(window.location.search);
const online = (h: Host) => h.state === "online" || h.state === "busy";

/** Machines workbench: machine list on the left, the selected machine's tools on the right (full page, no sheets). */
export function FleetPage({ ws, go }: { ws: string; go: (p: string) => void }) {
  const qc = useQueryClient();
  const q = search();
  const tab = (q.get("tab") as Tab) || "machines";
  const open = q.get("new") === "1";
  const hosts = useQuery({ queryKey: ["hosts", ws], queryFn: () => api.fleet.listHosts({ workspaceId: ws }), refetchInterval: 5000 });
  const upgrades = useQuery({ queryKey: ["agent-upgrades", ws], queryFn: () => api.fleet.listAgentUpgrades({ workspaceId: ws }), refetchInterval: 3000 });
  const upgrade = useMutation({
    mutationFn: (v: { hostIds: string[]; force?: boolean }) => api.fleet.upgradeAgents({ workspaceId: ws, hostIds: v.hostIds, force: v.force ?? false }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["agent-upgrades", ws] }),
  });
  const list = hosts.data?.hosts ?? [];
  const selected = list.find((h) => h.id === q.get("host")) ?? list[0];
  const hostTab = (q.get("view") as HostTab) || "overview";
  const select = (id: string, view: HostTab = hostTab) => go(`fleet?host=${id}&view=${view}`);
  const running = new Set((upgrades.data?.upgrades ?? []).filter((u) => u.state === "queued" || u.state === "running").map((u) => u.hostId));
  return (
    <div className="flex h-full min-h-0 flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <Tabs<Tab> value={tab} onChange={(t) => go(`fleet?tab=${t}`)} tabs={[
          { key: "machines", label: t("Machines"), count: list.length },
          { key: "map", label: t("Cluster map") },
          { key: "ray", label: "Ray" },
          { key: "data", label: t("Data on machines") },
          { key: "agents", label: t("Agent updates"), count: running.size || undefined },
        ]} />
        <button className="btn-primary" onClick={() => go("fleet?new=1")}>{t("+ Add machine")}</button>
      </div>
      <ErrorBox error={hosts.error ? errorText(hosts.error) : upgrade.error ? errorText(upgrade.error) : null} onRetry={() => void hosts.refetch()} />
      {tab === "map" ? <ClusterMapView ws={ws} go={go} />
        : tab === "data" ? <DataCacheList ws={ws} go={go} />
        : tab === "ray" ? <RayHealthView ws={ws} />
        : tab === "agents" ? <UpgradeHistory items={upgrades.data?.upgrades ?? []} latest={list[0]?.latestAgentVersion} loading={upgrades.isLoading} />
        : hosts.isLoading ? <Skeleton rows={6} />
        : list.length === 0 ? <EmptyState title={t("No machines yet")} action={<button className="btn-primary" onClick={() => go("fleet?new=1")}>{t("Add your first machine")}</button>} />
        : (
          <div className="grid min-h-0 flex-1 gap-4 lg:grid-cols-[300px_1fr]">
            <MachineList ws={ws} hosts={list} selectedId={selected?.id} onSelect={(id) => select(id)} />
            {selected && <MachineWorkspace ws={ws} h={selected} tab={hostTab} onTab={(v) => select(selected.id, v)} busy={running.has(selected.id)}
              onUpgrade={(force) => upgrade.mutate({ hostIds: [selected.id], force })} />}
          </div>)}
      {open && <EnrollDialog ws={ws} onClose={() => go("fleet")} />}
    </div>
  );
}

function MiniBar({ pct, tone }: { pct: number; tone: string }) {
  return <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-surface-2"><div className={`h-full ${tone}`} style={{ width: `${Math.max(0, Math.min(100, pct))}%` }} /></div>;
}

function MachineList({ ws, hosts, selectedId, onSelect }: { ws: string; hosts: Host[]; selectedId?: string; onSelect: (id: string) => void }) {
  return (
    <div className="flex min-h-0 flex-col gap-2 overflow-y-auto">
      {hosts.map((h) => <MachineRow key={h.id} ws={ws} h={h} active={h.id === selectedId} onClick={() => onSelect(h.id)} />)}
    </div>
  );
}

function MachineRow({ ws, h, active, onClick }: { ws: string; h: Host; active: boolean; onClick: () => void }) {
  const gpus = useQuery({ queryKey: ["host-gpus", ws, h.id], queryFn: () => api.fleet.getHostGpus({ workspaceId: ws, hostId: h.id }),
    refetchInterval: 15000, enabled: (h.hostInfo?.gpus.length ?? 0) > 0 && online(h) });
  return (
    <button onClick={onClick} className={`w-full rounded-lg border p-3 text-left transition ${active ? "border-accent bg-accent/10" : "border-line bg-surface hover:bg-surface-2"}`}>
      <div className="flex items-center gap-2">
        <span className={`h-2.5 w-2.5 shrink-0 rounded-full ${online(h) ? "bg-success" : h.state === "degraded" ? "bg-warning" : "bg-fg-subtle"}`} />
        <span className="min-w-0 flex-1 truncate text-base font-medium">{h.name}</span>
        {h.updateAvailable && <span className="badge bg-sky-100 text-sky-700">{t("update")}</span>}
      </div>
      <div className="mt-1 truncate text-sm text-fg-muted">{h.hostInfo?.logicalCpus ?? "—"} CPU · {h.hostInfo ? bytes(h.hostInfo.memoryBytes) : "—"} RAM{h.hostInfo?.instance ? ` · ${h.hostInfo.instance}` : ""}</div>
      {(gpus.data?.gpus ?? []).map((g) => {
        const s = g.sample;
        const total = Number(s?.memoryTotalBytes || g.info?.memoryBytes || 1n);
        return (
          <div key={g.info?.uuid} className="mt-2 space-y-1 text-sm">
            <div className="flex justify-between text-fg-muted"><span className="truncate">GPU {g.info?.index} · {g.info?.name}</span><span className="tabular-nums">{s?.temperatureC.toFixed(0)}°C</span></div>
            <div className="flex items-center gap-2"><span className="w-12 text-xs text-fg-subtle">{t("Util")}</span><MiniBar pct={s?.utilPercent ?? 0} tone="bg-accent" /><span className="w-10 text-right text-xs tabular-nums">{s?.utilPercent.toFixed(0)}%</span></div>
            <div className="flex items-center gap-2"><span className="w-12 text-xs text-fg-subtle">VRAM</span><MiniBar pct={(Number(s?.memoryUsedBytes ?? 0n) / total) * 100} tone="bg-violet-400" /><span className="w-10 text-right text-xs tabular-nums">{((Number(s?.memoryUsedBytes ?? 0n) / total) * 100).toFixed(0)}%</span></div>
          </div>);
      })}
    </button>
  );
}

function MachineWorkspace({ ws, h, tab, onTab, busy, onUpgrade }:
  { ws: string; h: Host; tab: HostTab; onTab: (t: HostTab) => void; busy: boolean; onUpgrade: (force: boolean) => void }) {
  const live = online(h);
  return (
    <div className="flex min-h-0 flex-col gap-3 rounded-lg border border-line bg-surface p-4">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-xl font-semibold">{h.name}</h2>
        <StateBadge state={h.state} reason={h.stateReason || undefined} />
        <span className="text-sm text-fg-muted">{h.hostInfo?.osDescription}</span>
      </div>
      <Tabs<HostTab> value={tab} onChange={onTab} tabs={[
        { key: "overview", label: t("Overview") },
        { key: "gpu", label: "GPU", count: h.hostInfo?.gpus.length || undefined },
        { key: "terminal", label: t("Terminal") },
        { key: "files", label: t("Files") },
        { key: "processes", label: t("Processes") },
        { key: "services", label: t("Services") },
        { key: "activity", label: t("Activity") },
      ]} />
      <div className="min-h-0 flex-1 overflow-y-auto">
        {tab === "overview" ? <MachineOverview h={h} busy={busy} onUpgrade={onUpgrade} />
          : tab === "activity" ? <HostActivityPanel ws={ws} h={h} />
          : !live ? <EmptyState bare title={t("The machine is offline.")} />
          : tab === "gpu" ? <GpuPanel ws={ws} hostId={h.id} />
          : tab === "terminal" ? <TerminalPanel key={h.id} ws={ws} hostId={h.id} />
          : tab === "files" ? <FilesPanel key={h.id} ws={ws} hostId={h.id} />
          : tab === "processes" ? <ProcessesPanel ws={ws} hostId={h.id} />
          : <ServicesPanel ws={ws} hostId={h.id} />}
      </div>
    </div>
  );
}

function Fact({ label, value }: { label: string; value: React.ReactNode }) {
  return <div className="rounded-lg border border-line p-3"><div className="text-xs text-fg-subtle">{label}</div><div className="mt-1 text-base">{value}</div></div>;
}

function MachineOverview({ h, busy, onUpgrade }: { h: Host; busy: boolean; onUpgrade: (force: boolean) => void }) {
  const i = h.hostInfo;
  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Fact label="CPU" value={`${i?.logicalCpus ?? "—"}`} />
        <Fact label="RAM" value={i ? bytes(i.memoryBytes) : "—"} />
        <Fact label="GPU" value={i?.gpus.length ? i.gpus.map((g) => `${g.name} · ${bytes(g.memoryBytes)}`).join(", ") : t("no GPU")} />
        <Fact label="Docker" value={i?.dockerVersion || "—"} />
        <Fact label="VPN" value={<span className="mono">{h.overlayIp || "—"} · {h.vpnStatus || "—"}</span>} />
        <Fact label={t("heartbeat")} value={<RelTime at={h.lastSeenAt} />} />
        <Fact label={t("GPU priority")} value={`${i?.instance || t("default")} · ${t(i?.gpuPriority || "normal")}`} />
        <Fact label="Agent" value={<span className="mono">{h.agentVersion.slice(0, 10) || "—"}</span>} />
      </div>
      <div className="flex items-center gap-2">
        {busy ? <span className="badge bg-sky-100 text-sky-700">{t("updating…")}</span>
          : h.updateAvailable ? <button className="btn-primary" onClick={() => onUpgrade(false)}><RefreshCw className="h-4 w-4" />{t("Update agent")}</button>
          : <button className="btn-ghost" disabled={!online(h)} onClick={() => onUpgrade(true)}><RefreshCw className="h-4 w-4" />{t("Reinstall")}</button>}
      </div>
      {(i?.fsRoots.length ?? 0) > 0 && <Fact label={t("Allowed folders")} value={<span className="mono text-sm">{i!.fsRoots.join("  ·  ")}</span>} />}
    </div>
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
