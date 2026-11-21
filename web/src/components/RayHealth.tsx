import { useQuery } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ago, bytes } from "../lib/format";
import { translate as t } from "../i18n";
import { ErrorBox } from "./ui";

/** One used/total meter (CPU, GPU, memory). */
function Meter({ used, total, fmt }: { used: number; total: number; fmt?: (n: number) => string }) {
  if (!total) return <span className="text-fg-subtle">—</span>;
  const p = Math.min(100, (used / total) * 100);
  const f = fmt ?? ((n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(1)));
  return (
    <div className="min-w-[7rem]">
      <div className="h-1.5 overflow-hidden rounded-full bg-surface-2"><div className={`h-full ${p > 90 ? "bg-danger" : p > 70 ? "bg-warning" : "bg-primary"}`} style={{ width: `${p}%` }} /></div>
      <div className="mt-0.5 text-[11px] tabular-nums text-fg-muted">{f(used)} / {f(total)}</div>
    </div>
  );
}

const idle = (s: number) => (s >= 3600 ? `${Math.floor(s / 3600)} h` : s >= 60 ? `${Math.floor(s / 60)} m` : `${Math.round(s)} s`);

/** Machines page: Ray cluster health — nodes alive/dead with resource use, placement groups, demands Ray cannot place. */
export function RayHealthView({ ws }: { ws: string }) {
  const q = useQuery({ queryKey: ["ray-health", ws], queryFn: () => api.ops.getRayHealth({ workspaceId: ws }), refetchInterval: 5000 });
  const h = q.data;
  if (q.isLoading) return <div className="skeleton h-48" />;
  if (q.error) return <ErrorBox error={errorText(q.error)} />;
  if (!h) return null;
  const alive = h.nodes.filter((n) => n.state === "ALIVE").length;
  const dead = h.nodes.length - alive;
  let pending: { resources?: unknown[]; placement_groups?: unknown[] } = {};
  try { pending = JSON.parse(h.pendingJson || "{}"); } catch { pending = {}; }
  const waiting = (pending.resources?.length ?? 0) + (pending.placement_groups?.length ?? 0);
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <div className="card"><div className="text-xs text-fg-muted">Ray head</div><div className={`mt-1 text-lg font-semibold ${h.reachable ? "text-success" : "text-danger"}`}>{h.reachable ? t("Reachable") : t("No answer")}</div><div className="text-xs text-fg-subtle">{h.rayVersion}</div></div>
        <div className="card"><div className="text-xs text-fg-muted">{t("Nodes alive")}</div><div className="mt-1 text-lg font-semibold tabular-nums">{alive}</div></div>
        <div className="card"><div className="text-xs text-fg-muted">{t("Nodes dead")}</div><div className={`mt-1 text-lg font-semibold tabular-nums ${dead ? "text-danger" : ""}`}>{dead}</div></div>
        <div className="card"><div className="text-xs text-fg-muted">{t("Waiting demands")}</div><div className={`mt-1 text-lg font-semibold tabular-nums ${waiting ? "text-warning" : ""}`}>{waiting}</div></div>
      </div>
      <ErrorBox error={h.error ? t(h.error) : null} />
      <div className="card overflow-x-auto p-0">
        <table className="w-full min-w-max text-sm [&_td]:px-3 [&_td]:py-2.5 [&_th]:px-3 [&_th]:py-2">
          <thead className="text-left text-xs text-fg-muted"><tr>
            <th>{t("Machine")}</th><th>IP</th><th>{t("State")}</th><th>CPU</th><th>GPU</th><th>RAM</th><th>Object store</th><th>{t("Idle")}</th><th>{t("Up since")}</th>
          </tr></thead>
          <tbody>{h.nodes.map((n) => (
            <tr key={n.nodeId} className="border-t border-line align-top">
              <td><div className="font-medium">{n.isHead ? "head" : n.hostName || n.hostId.slice(0, 8) || "—"}</div><div className="font-mono text-[11px] text-fg-subtle">{n.nodeId.slice(0, 12)}</div></td>
              <td className="font-mono text-xs">{n.ip}</td>
              <td><span className={`rounded-md px-2 py-0.5 text-xs ${n.state === "ALIVE" ? "bg-success/15 text-success" : "bg-danger/15 text-danger"}`}>{n.state}</span>
                {n.stateMessage && <div className="mt-1 max-w-xs whitespace-normal text-xs text-danger">{n.stateMessage}</div>}</td>
              <td><Meter used={n.cpuUsed} total={n.cpuTotal} /></td>
              <td><Meter used={n.gpuUsed} total={n.gpuTotal} /></td>
              <td><Meter used={n.memoryUsed} total={n.memoryTotal} fmt={(x) => bytes(x)} /></td>
              <td><Meter used={n.objectStoreUsed} total={n.objectStoreTotal} fmt={(x) => bytes(x)} /></td>
              <td className="tabular-nums text-xs text-fg-muted">{n.state === "ALIVE" && n.idleSeconds > 0 ? idle(n.idleSeconds) : "—"}</td>
              <td className="text-xs text-fg-muted">{n.endedAt ? `${t("down")} ${ago(n.endedAt)}` : n.startedAt ? ago(n.startedAt) : "—"}</td>
            </tr>))}</tbody>
        </table>
      </div>
      <div className="card">
        <div className="mb-2 text-sm font-medium">{t("Placement groups")} <span className="text-fg-subtle">{h.placementGroups.length}</span></div>
        {h.placementGroups.length === 0 ? <div className="text-sm text-fg-subtle">—</div> : (
          <ul className="divide-y divide-line text-sm">{h.placementGroups.map((g) => (
            <li key={g.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2">
              <span className={`rounded-md px-2 py-0.5 text-xs ${g.state === "CREATED" ? "bg-success/15 text-success" : g.state === "PENDING" ? "bg-warning/15 text-warning" : "bg-surface-2 text-fg-muted"}`}>{g.state}</span>
              <span className="font-mono text-xs">{g.name || g.id.slice(0, 12)}</span><span className="text-xs text-fg-muted">{g.strategy}</span>
              <span className="break-all font-mono text-[11px] text-fg-subtle">{g.bundlesJson}</span>
            </li>))}</ul>)}
        {waiting > 0 && <pre className="mt-3 overflow-x-auto rounded-lg bg-surface-2 p-2 text-[11px] text-warning">{JSON.stringify(pending, null, 1)}</pre>}
      </div>
    </div>
  );
}
