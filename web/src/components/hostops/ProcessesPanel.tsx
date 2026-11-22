import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../../lib/api";
import type { ProcessInfo } from "../../gen/mlplatform/v1/agent_pb";
import { bytes } from "../../lib/format";
import { ConfirmDialog, ErrorBox, RelTime, Skeleton, Tabs } from "../ui";
import { translate as t } from "../../i18n";

type Sort = "cpu" | "memory";

/** Processes on the machine by CPU or memory, with the container each one runs in; stop or kill. */
export function ProcessesPanel({ ws, hostId }: { ws: string; hostId: string }) {
  const qc = useQueryClient();
  const [sort, setSort] = useState<Sort>("cpu");
  const [filter, setFilter] = useState("");
  const [target, setTarget] = useState<{ p: ProcessInfo; force: boolean } | null>(null);
  const q = useQuery({ queryKey: ["host-procs", ws, hostId, sort], queryFn: () => api.fleet.listHostProcesses({ workspaceId: ws, hostId, sort }), refetchInterval: 5000 });
  const kill = useMutation({
    mutationFn: (v: { pid: number; force: boolean }) => api.fleet.killHostProcess({ workspaceId: ws, hostId, pid: v.pid, force: v.force, tree: v.force }),
    onSuccess: () => { setTarget(null); void qc.invalidateQueries({ queryKey: ["host-procs", ws, hostId] }); },
  });
  const f = filter.toLowerCase();
  const rows = (q.data?.processes ?? []).filter((p) => !f || `${p.name} ${p.command} ${p.container} ${p.user} ${p.pid}`.toLowerCase().includes(f));
  const maxCpu = Math.max(100, ...rows.map((p) => p.cpuPercent));
  return (
    <div className="space-y-3 p-3">
      <div className="flex flex-wrap items-center gap-3">
        <Tabs<Sort> value={sort} onChange={setSort} tabs={[{ key: "cpu", label: "CPU" }, { key: "memory", label: t("Memory") }]} />
        <input aria-label={t("Filter")} className="input h-8 max-w-xs" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder={t("Filter")} />
        <span className="text-xs text-fg-muted">{rows.length} / {q.data?.total ?? 0}</span>
      </div>
      <ErrorBox error={q.error ? errorText(q.error) : kill.error ? errorText(kill.error) : null} />
      {!q.data ? <Skeleton rows={8} /> : (
        <div className="overflow-x-auto rounded-md border border-line">
          <table className="w-full text-sm">
            <thead className="text-left text-xs text-fg-subtle"><tr>
              <th className="px-3 py-2">PID</th><th className="px-3 py-2">{t("Name")}</th><th className="px-3 py-2">{t("User")}</th><th className="px-3 py-2">{t("Container")}</th>
              <th className="px-3 py-2">CPU</th><th className="px-3 py-2 text-right">RSS</th><th className="px-3 py-2">{t("Started")}</th><th /></tr></thead>
            <tbody>{rows.map((p) => (
              <tr key={p.pid} className="group border-t border-line hover:bg-surface-2" title={p.command}>
                <td className="px-3 py-1.5 tabular-nums">{p.pid}</td>
                <td className="max-w-[16rem] truncate px-3 py-1.5">{p.name}</td>
                <td className="px-3 py-1.5 text-xs">{p.user || "—"}</td>
                <td className="mono px-3 py-1.5 text-xs">{p.container || ""}</td>
                <td className="w-32 px-3 py-1.5"><div className="flex items-center gap-2"><div className="h-1.5 flex-1 rounded-full bg-surface-2"><div className="h-full rounded-full bg-accent" style={{ width: `${(p.cpuPercent / maxCpu) * 100}%` }} /></div><span className="w-10 text-right text-xs tabular-nums">{p.cpuPercent.toFixed(1)}</span></div></td>
                <td className="whitespace-nowrap px-3 py-1.5 text-right tabular-nums">{bytes(p.rssBytes)}</td>
                <td className="px-3 py-1.5 text-xs text-fg-muted"><RelTime at={p.startedAt} /></td>
                <td className="px-3 py-1.5 text-right"><span className="invisible inline-flex gap-1 group-hover:visible">
                  <button className="btn-ghost h-7 flex-none whitespace-nowrap px-2 text-xs" onClick={() => setTarget({ p, force: false })}>{t("Stop")}</button>
                  <button className="btn-ghost h-7 flex-none whitespace-nowrap px-2 text-xs text-danger" onClick={() => setTarget({ p, force: true })}>{t("Kill")}</button></span></td>
              </tr>))}</tbody>
          </table>
        </div>)}
      {target && <ConfirmDialog title={`${t(target.force ? "Kill" : "Stop")} ${target.p.name} (${target.p.pid})`} consequence={<span className="mono break-all text-xs">{target.p.command || target.p.name}</span>}
        label={target.force ? "Kill" : "Stop"} busy={kill.isPending} onConfirm={() => kill.mutate({ pid: target.p.pid, force: target.force })} onClose={() => setTarget(null)} />}
    </div>
  );
}
