import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../../lib/api";
import type { ServiceInfo } from "../../gen/mlplatform/v1/agent_pb";
import { ConfirmDialog, Dialog, ErrorBox, Skeleton } from "../ui";
import { translate as t } from "../../i18n";

const STATE: Record<string, string> = { active: "text-success", running: "text-success", failed: "text-danger", stopped: "text-fg-muted", inactive: "text-fg-muted" };
const btn = "inline-flex h-8 flex-none items-center whitespace-nowrap rounded-md border border-line px-3 text-sm hover:bg-surface-2 disabled:opacity-50";

function ServiceSheet({ ws, hostId, svc, onClose }: { ws: string; hostId: string; svc: ServiceInfo | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [action, setAction] = useState<string | null>(null);
  const unit = svc?.name ?? "";
  const log = useQuery({ queryKey: ["host-log", ws, hostId, unit], refetchInterval: 10000,
    queryFn: () => api.fleet.readHostSystemLog({ workspaceId: ws, hostId, unit, lines: 300 }) });
  const control = useMutation({
    mutationFn: (a: string) => api.fleet.controlHostService({ workspaceId: ws, hostId, name: unit, action: a }),
    onSuccess: () => { setAction(null); void qc.invalidateQueries({ queryKey: ["host-services", ws, hostId] }); },
  });
  return (
    <Dialog wide title={svc?.name ?? t("System log")} onClose={onClose}>
      <div className="flex h-full flex-col gap-3">
        {svc && (
          <div className="grid grid-cols-[120px_minmax(0,1fr)] gap-x-4 gap-y-2 text-sm">
            <span className="text-fg-muted">{t("Name")}</span><span className="truncate">{svc.description || svc.name}</span>
            <span className="text-fg-muted">{t("State")}</span><span className={STATE[svc.subState] ?? STATE[svc.state] ?? ""}>{svc.state}{svc.subState ? ` · ${svc.subState}` : ""}</span>
            <span className="text-fg-muted">{t("Start type")}</span><span>{svc.startType || "—"}</span>
          </div>)}
        {svc && (
          <div className="flex flex-wrap gap-2">
            {["start", "stop", "restart"].map((a) => <button key={a} className={btn} onClick={() => setAction(a)}>{t(a)}</button>)}
          </div>)}
        <ErrorBox error={control.error ? errorText(control.error) : log.error ? errorText(log.error) : null} />
        <pre className="mono min-h-0 flex-1 overflow-auto whitespace-pre-wrap break-all rounded-md border border-line bg-[#0b0e13] p-3 text-[11px] leading-5" style={{ color: "#e6e9ef" }}>
          {log.data ? log.data.lines.join("\n") : t("Loading…")}</pre>
      </div>
      {action && <ConfirmDialog title={`${t(action)} ${unit}`} consequence={<span className="mono text-xs">{unit}</span>} label={action}
        busy={control.isPending} onConfirm={() => control.mutate(action)} onClose={() => setAction(null)} />}
    </Dialog>
  );
}

/** System services; a service opens in a right-hand sheet with its controls and log. */
export function ServicesPanel({ ws, hostId }: { ws: string; hostId: string }) {
  const [filter, setFilter] = useState("");
  const [open, setOpen] = useState<{ svc: ServiceInfo | null } | null>(null);
  const q = useQuery({ queryKey: ["host-services", ws, hostId], queryFn: () => api.fleet.listHostServices({ workspaceId: ws, hostId }) });
  const f = filter.toLowerCase();
  const rows = (q.data?.services ?? []).filter((s) => !f || `${s.name} ${s.description}`.toLowerCase().includes(f));
  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex h-12 flex-none items-center gap-2 border-b border-line px-2">
        <input aria-label={t("Filter")} className="input h-8 min-w-0 flex-1 px-2" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder={t("Filter")} />
        <span className="flex-none whitespace-nowrap text-xs text-fg-muted">{rows.length} · {q.data?.manager}</span>
        <button className={btn} onClick={() => setOpen({ svc: null })}>{t("System log")}</button>
      </div>
      <ErrorBox error={q.error ? errorText(q.error) : null} />
      {!q.data ? <Skeleton rows={8} /> : (
        <div className="min-h-0 flex-1 overflow-auto">
          <table className="w-full table-fixed text-sm">
            <colgroup><col /><col className="w-36" /><col className="w-28" /></colgroup>
            <tbody>{rows.map((s) => (
              <tr key={s.name} className="h-12 cursor-pointer border-b border-line hover:bg-surface-2" onClick={() => setOpen({ svc: s })}>
                <td className="px-3"><div className="mono truncate text-xs">{s.name}</div><div className="truncate text-xs text-fg-muted">{s.description}</div></td>
                <td className={`whitespace-nowrap px-3 text-xs ${STATE[s.subState] ?? STATE[s.state] ?? ""}`}>{s.state}{s.subState ? ` · ${s.subState}` : ""}</td>
                <td className="whitespace-nowrap px-3 text-xs text-fg-muted">{s.startType}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
      {open && <ServiceSheet ws={ws} hostId={hostId} svc={open.svc} onClose={() => setOpen(null)} />}
    </div>
  );
}
