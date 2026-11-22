import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../../lib/api";
import { ConfirmDialog, ErrorBox, Skeleton } from "../ui";
import { translate as t } from "../../i18n";

const STATE: Record<string, string> = { active: "text-success", running: "text-success", failed: "text-danger", stopped: "text-fg-muted", inactive: "text-fg-muted" };

/** System services with start/stop/restart and the tail of each one's log. */
export function ServicesPanel({ ws, hostId }: { ws: string; hostId: string }) {
  const qc = useQueryClient();
  const [filter, setFilter] = useState("");
  const [unit, setUnit] = useState<string | null>(null);
  const [action, setAction] = useState<{ name: string; action: string } | null>(null);
  const q = useQuery({ queryKey: ["host-services", ws, hostId], queryFn: () => api.fleet.listHostServices({ workspaceId: ws, hostId }) });
  const log = useQuery({ queryKey: ["host-log", ws, hostId, unit], enabled: unit !== null, refetchInterval: 10000,
    queryFn: () => api.fleet.readHostSystemLog({ workspaceId: ws, hostId, unit: unit ?? "", lines: 300 }) });
  const control = useMutation({
    mutationFn: (v: { name: string; action: string }) => api.fleet.controlHostService({ workspaceId: ws, hostId, ...v }),
    onSuccess: () => { setAction(null); void qc.invalidateQueries({ queryKey: ["host-services", ws, hostId] }); },
  });
  const f = filter.toLowerCase();
  const rows = (q.data?.services ?? []).filter((s) => !f || `${s.name} ${s.description}`.toLowerCase().includes(f));
  return (
    <div className="grid gap-4 xl:grid-cols-2">
      <div className="space-y-3">
        <div className="flex items-center gap-3">
          <input className="input h-8 max-w-xs" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder={t("Filter")} />
          <span className="text-xs text-fg-muted">{q.data?.manager}</span>
          <button className="btn-ghost h-8 px-2 text-xs" onClick={() => setUnit("")}>{t("System log")}</button>
        </div>
        <ErrorBox error={q.error ? errorText(q.error) : control.error ? errorText(control.error) : null} />
        {!q.data ? <Skeleton rows={8} /> : (
          <div className="card max-h-[65vh] overflow-auto p-0">
            <table className="w-full text-sm">
              <tbody>{rows.map((s) => (
                <tr key={s.name} className={`group border-t border-line hover:bg-surface-2 ${unit === s.name ? "bg-surface-2" : ""}`}>
                  <td className="px-3 py-1.5"><button className="text-left" onClick={() => setUnit(s.name)}>
                    <div className="mono text-xs">{s.name}</div><div className="truncate text-xs text-fg-muted">{s.description}</div></button></td>
                  <td className={`px-3 py-1.5 text-xs ${STATE[s.subState] ?? STATE[s.state] ?? ""}`}>{s.state}{s.subState ? ` · ${s.subState}` : ""}</td>
                  <td className="px-3 py-1.5 text-xs text-fg-muted">{s.startType}</td>
                  <td className="px-3 py-1.5 text-right"><span className="invisible inline-flex gap-1 group-hover:visible">
                    {["start", "stop", "restart"].map((a) => <button key={a} className="btn-ghost h-7 px-2 text-xs" onClick={() => setAction({ name: s.name, action: a })}>{t(a)}</button>)}
                  </span></td>
                </tr>))}</tbody>
            </table>
          </div>)}
      </div>
      {unit !== null && (
        <div className="space-y-2">
          <div className="mono text-xs text-fg-muted">{unit || t("System log")}</div>
          <ErrorBox error={log.error ? errorText(log.error) : null} />
          <pre className="mono max-h-[65vh] overflow-auto whitespace-pre-wrap break-all rounded-lg border border-line bg-[#0b0e13] p-3 text-[11px] leading-5" style={{ color: "#e6e9ef" }}>
            {log.data?.lines.join("\n") ?? ""}</pre>
        </div>)}
      {action && <ConfirmDialog title={`${t(action.action)} ${action.name}`} consequence={<span className="mono text-xs">{action.name}</span>} label={action.action}
        busy={control.isPending} onConfirm={() => control.mutate(action)} onClose={() => setAction(null)} />}
    </div>
  );
}
