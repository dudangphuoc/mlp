import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { OsFamily } from "../gen/mlplatform/v1/agent_pb";
import type { AgentUpgrade, Host } from "../gen/mlplatform/v1/fleet_pb";
import { bytes } from "../lib/format";
import {
  CommandBlock, Dialog, EmptyState, ErrorBox, Field, HardwareChip, PageHeader, ProgressSteps, RelTime, Skeleton, StateBadge, Tabs, Tip,
} from "../components/ui";
import { ClusterMapView } from "./Ops";
import { DataCacheList } from "../components/DataSync";
import { RayHealthView } from "../components/RayHealth";
import { translate as t } from "../i18n";
import { Activity, CheckCircle2, CircleX, Clock, Loader2, RefreshCw, TriangleAlert } from "lucide-react";

type Tab = "list" | "map" | "ray" | "data" | "agents";
const search = () => new URLSearchParams(window.location.search);

/** S-02-1 Machine list, S-02-2 Add machine, S-02-5 Cluster map + OTA agent updates (D-38). */
export function FleetPage({ ws, go }: { ws: string; go: (p: string) => void }) {
  const qc = useQueryClient();
  const tab = (search().get("tab") as Tab) || "list";
  const open = search().get("new") === "1";
  const [activity, setActivity] = useState<Host | null>(null);
  const hosts = useQuery({ queryKey: ["hosts", ws], queryFn: () => api.fleet.listHosts({ workspaceId: ws }), refetchInterval: 5000 });
  const upgrades = useQuery({ queryKey: ["agent-upgrades", ws], queryFn: () => api.fleet.listAgentUpgrades({ workspaceId: ws }), refetchInterval: 3000 });
  const upgrade = useMutation({
    mutationFn: (v: { hostIds: string[]; force?: boolean }) => api.fleet.upgradeAgents({ workspaceId: ws, hostIds: v.hostIds, force: v.force ?? false }),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["agent-upgrades", ws] }); go("fleet?tab=agents"); },
  });
  const list = hosts.data?.hosts ?? [];
  const online = list.filter((h) => h.state === "online" || h.state === "busy").length;
  const outdated = list.filter((h) => h.updateAvailable);
  const running = new Set((upgrades.data?.upgrades ?? []).filter((u) => u.state === "queued" || u.state === "running").map((u) => u.hostId));
  return (
    <>
      <PageHeader title={t("Machines (Fleet)")} subtitle={t("{a}/{b} machines online · {g} GPU", { a: online, b: list.length, g: list.reduce((n, h) => n + (h.hostInfo?.gpus.length ?? 0), 0) })}
        actions={<>
          <button className="btn-ghost" disabled={upgrade.isPending || outdated.length === 0} onClick={() => upgrade.mutate({ hostIds: [] })}
            title={outdated.length === 0 ? t("Every machine runs the latest agent") : undefined}>
            <RefreshCw className="h-4 w-4" />{outdated.length > 0 ? t("Update agent ({n} machines)", { n: outdated.length }) : t("Agent up to date")}</button>
          <button className="btn-primary" onClick={() => go("fleet?new=1")}>{t("+ Add machine")}</button>
        </>} />
      <ErrorBox error={hosts.error ? errorText(hosts.error) : upgrade.error ? errorText(upgrade.error) : null} onRetry={() => void hosts.refetch()} />
      <Tabs<Tab> value={tab} onChange={(t) => go(`fleet?tab=${t}`)} tabs={[
        { key: "list", label: t("List"), count: list.length },
        { key: "map", label: t("Cluster map") },
        { key: "ray", label: "Ray" },
        { key: "data", label: t("Data on machines") },
        { key: "agents", label: t("Update agent"), count: running.size || undefined },
      ]} />
      {tab === "map" ? <ClusterMapView ws={ws} go={go} />
        : tab === "data" ? <DataCacheList ws={ws} go={go} />
        : tab === "ray" ? <RayHealthView ws={ws} />
        : tab === "agents" ? <UpgradeHistory items={upgrades.data?.upgrades ?? []} latest={list[0]?.latestAgentVersion} loading={upgrades.isLoading} />
        : hosts.isLoading ? <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 min-[2200px]:grid-cols-5">{[0, 1, 2].map((i) => <div key={i} className="skeleton h-44" />)}</div>
        : list.length === 0 ? (
          <EmptyState title={t("No machines yet")} action={<button className="btn-primary" onClick={() => go("fleet?new=1")}>{t("Add your first machine")}</button>} />
        ) : (
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 min-[2200px]:grid-cols-5">
            {list.map((h) => <HostCard key={h.id} h={h} busy={running.has(h.id)} onUpgrade={(force) => upgrade.mutate({ hostIds: [h.id], force })} onActivity={() => setActivity(h)} />)}
          </div>
        )}
      {open && <EnrollDialog ws={ws} onClose={() => go("fleet")} />}
      {activity && <HostActivitySheet ws={ws} h={activity} onClose={() => setActivity(null)} />}
    </>
  );
}

function HostCard({ h, busy, onUpgrade, onActivity }: { h: Host; busy: boolean; onUpgrade: (force: boolean) => void; onActivity: () => void }) {
  const i = h.hostInfo;
  return (
    <div className="card space-y-3">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="truncate font-medium">{h.name}</div>
          <div className="truncate text-xs text-fg-subtle">{i?.osDescription ?? "—"}</div>
        </div>
        <StateBadge state={h.state} reason={h.stateReason || undefined} />
      </div>
      {h.state === "degraded" && h.stateReason && (
        <div className="flex items-start gap-2 rounded-md border border-warning/30 bg-warning/10 px-2.5 py-2 text-xs text-warning">
          <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" /><span className="line-clamp-3 break-words">{t(h.stateReason)}</span>
        </div>)}
      <div className="flex flex-wrap gap-1.5">
        {i?.gpus.length ? i.gpus.map((g) => <HardwareChip key={g.uuid}>▮ {g.name} · {bytes(g.memoryBytes)}</HardwareChip>) : <HardwareChip>{t("no GPU")}</HardwareChip>}
        <HardwareChip>{i?.logicalCpus ?? "—"} CPU</HardwareChip>
        <HardwareChip>{i ? bytes(i.memoryBytes) : "—"} RAM</HardwareChip>
      </div>
      <div className="flex items-center justify-between gap-2 border-t border-line pt-2 text-xs text-fg-muted">
        <Tip text={h.vpnMessage || t("Not checked yet")}><span className="inline-flex items-center gap-1.5">
          <span className={`h-1.5 w-1.5 rounded-full ${h.vpnStatus === "connected" ? "bg-success" : h.vpnStatus === "failed" ? "bg-danger" : "bg-warning"}`} />
          <span className="mono">VPN {h.overlayIp || "—"}</span>{h.vpnStatus === "failed" && <span className="text-danger">{t("not connected")}</span>}
        </span></Tip>
        <span>{t("heartbeat")} <RelTime at={h.lastSeenAt} /></span>
      </div>
      <div className="flex items-center justify-between gap-2 text-xs">
        <button className="btn-ghost h-7 px-2 text-xs" onClick={onActivity}><Activity className="h-3.5 w-3.5" />{t("Activity")}</button>
        <Tip text={`Agent ${h.agentVersion} · ${i?.agentRid ?? ""} · ${i?.serviceManager ?? ""}`}>
          <span className="mono text-fg-muted">agent {h.agentVersion.slice(0, 10) || "—"}</span>
        </Tip>
        {busy ? <span className="badge bg-sky-100 text-sky-700">{t("updating…")}</span>
          : h.updateAvailable ? <button className="btn-primary h-7 px-2 text-xs" onClick={() => onUpgrade(false)}><RefreshCw className="h-3.5 w-3.5" />{t("Update agent")}</button>
          : <span className="inline-flex items-center gap-2"><span className="text-success">{t("✓ latest")}</span>
              <button className="btn-ghost h-7 px-2 text-xs" disabled={h.state === "offline"} onClick={() => onUpgrade(true)}><RefreshCw className="h-3.5 w-3.5" />{t("Reinstall")}</button></span>}
      </div>
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
function HostActivitySheet({ ws, h, onClose }: { ws: string; h: Host; onClose: () => void }) {
  const q = useQuery({ queryKey: ["host-activity", ws, h.id], queryFn: () => api.fleet.getHostActivity({ workspaceId: ws, hostId: h.id }), refetchInterval: 4000 });
  const a = q.data;
  return (
    <Dialog wide title={t("Activity on {name}", { name: h.name })} onClose={onClose}>
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
    </Dialog>
  );
}
