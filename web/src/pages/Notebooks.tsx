import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ago } from "../lib/format";
import { ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, PageHeader, StateBadge } from "../components/ui";
import { DataTable } from "../components/DataTable";
import { translate as t } from "../i18n";
import { useCrumb } from "../lib/crumbs";
import { ArrowLeft, Clock, ExternalLink, Play, Square, Trash2 } from "lucide-react";

const steps = ["queued", "starting", "running"];
/** "Keep 1 more hour" moves the activity mark into the future → shown as being kept. */
const activity = (at?: { seconds: bigint }) =>
  !at ? "—" : Number(at.seconds) * 1000 > Date.now() ? t("kept longer") : ago(at as Parameters<typeof ago>[0]);
const stepLabel: Record<string, string> = { queued: "Queued", starting: "Allocate + start", running: "Open Jupyter" };

/** S-08-1 Notebooks + S-08-2 Create notebook. */
export function NotebooksPage({ ws, onOpen }: { ws: string; onOpen: (id: string) => void }) {
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ["notebooks", ws], queryFn: () => api.notebook.listServers({ workspaceId: ws, all: false }), refetchInterval: 3000 });
  const profiles = useQuery({ queryKey: ["nb-profiles", ws], queryFn: () => api.notebook.listProfiles({ workspaceId: ws }) });
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ name: "", profile: "base", cpus: 2, memoryGb: 4, gpus: 0, idleTimeoutMin: 60, hostId: "" });
  const hosts = useQuery({ queryKey: ["hosts", ws], queryFn: () => api.fleet.listHosts({ workspaceId: ws }), enabled: open, refetchInterval: open ? 5000 : false });
  const hostList = hosts.data?.hosts ?? [];
  const create = useMutation({
    mutationFn: () => api.notebook.createServer({ workspaceId: ws, ide: "jupyterlab", ...f }),
    onSuccess: (n) => { qc.invalidateQueries({ queryKey: ["notebooks", ws] }); setOpen(false); onOpen(n.id); },
  });
  const stop = useMutation({
    mutationFn: (id: string) => api.notebook.stopServer({ workspaceId: ws, serverId: id }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["notebooks", ws] }),
  });
  const start = useMutation({
    mutationFn: (id: string) => api.notebook.startServer({ workspaceId: ws, serverId: id, hostId: "" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["notebooks", ws] }),
  });
  const del = useMutation({
    mutationFn: (id: string) => api.notebook.deleteServer({ workspaceId: ws, serverId: id }),
    onSuccess: () => { setDelId(null); void qc.invalidateQueries({ queryKey: ["notebooks", ws] }); },
  });
  const [delId, setDelId] = useState<string | null>(null);
  const servers = list.data?.servers ?? [];
  return (
    <>
      <PageHeader title="Notebooks"
        actions={<button className="btn-primary" onClick={() => setOpen(true)}>+ Notebook</button>} />
      <ErrorBox error={list.error ? errorText(list.error) : stop.error ? errorText(stop.error) : start.error ? errorText(start.error) : del.error ? errorText(del.error) : null} />
      {servers.length === 0 && !list.isLoading ? (
        <EmptyState title={t("No notebooks yet. Create one with a prebuilt environment.")}>
          <button className="btn-primary" onClick={() => setOpen(true)}>{t("Create notebook")}</button>
        </EmptyState>
      ) : (
        <DataTable rows={servers} loading={list.isLoading} rowKey={(n) => n.id} onRowClick={(n) => onOpen(n.id)}
          columns={[
            { key: "name", header: t("Name"), value: (n) => n.name, cell: (n) => <span className="font-medium">{n.name}</span> },
            { key: "state", header: t("Status"), value: (n) => n.state, cell: (n) => <StateBadge state={n.state} reason={n.stateReason || undefined} /> },
            { key: "profile", header: "Profile", value: (n) => n.profile, cell: (n) => <span className="text-fg-muted">{n.profile}</span> },
            { key: "res", header: t("Resources"), cell: (n) => <span className="text-xs text-fg-muted">{n.cpus} CPU · {n.memoryGb} GB{n.gpus ? ` · ${n.gpus} GPU` : ""}</span> },
            { key: "act-at", header: t("Activity"), value: (n) => Number(n.lastActivityAt?.seconds ?? 0), cell: (n) => <span className="text-xs text-fg-muted">{activity(n.lastActivityAt)}</span> },
            { key: "act", header: "", align: "right", cell: (n) => (
              <span className="inline-flex gap-1" onClick={(e) => e.stopPropagation()}>
                {n.state === "running" && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => onOpen(n.id)}>{t("Open")}</button>}
                {["queued", "starting", "running"].includes(n.state) && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => stop.mutate(n.id)}><Square className="h-3.5 w-3.5" />{t("Stop")}</button>}
                {n.state === "stopping" &&
                  <button className="btn-ghost h-7 px-2 text-xs text-warning" disabled={stop.isPending} onClick={() => stop.mutate(n.id)}><Square className="h-3.5 w-3.5" />{t("Force stop")}</button>}
                {["stopped", "failed"].includes(n.state) && <>
                  <button className="btn-ghost h-7 px-2 text-xs" disabled={start.isPending} onClick={() => start.mutate(n.id)}><Play className="h-3.5 w-3.5" />{t("Start again")}</button>
                  <button className="btn-ghost h-7 px-2 text-xs text-danger" onClick={() => setDelId(n.id)}><Trash2 className="h-3.5 w-3.5" />{t("Delete")}</button>
                </>}
              </span>) },
          ]} />)}
      {delId && (
        <ConfirmDialog title={t("Delete notebook?")} label={t("Delete")} busy={del.isPending} onClose={() => setDelId(null)} onConfirm={() => del.mutate(delId)}
          consequence={t("The notebook is removed from the list. Files in your home on the machine are kept and can be used by other notebooks.")} />)}
      {open && (
        <Dialog title={t("Create notebook")} onClose={() => setOpen(false)} wide>
          <div className="space-y-4">
            <div>
              <span className="label">{t("Environment")}</span>
              <div className="grid gap-2 md:grid-cols-4">
                {(profiles.data?.profiles ?? []).map((p) => (
                  <button key={p.name} className={`rounded-lg border p-2 text-left ${f.profile === p.name ? "border-brand-500 bg-brand-50" : "border-slate-200 hover:border-slate-300"}`}
                    onClick={() => setF({ ...f, profile: p.name })}>
                    <div className="text-sm font-medium">{p.displayName}</div>
                    <div className="line-clamp-2 text-xs text-slate-500">{t(p.description)}</div>
                  </button>))}
              </div>
            </div>
            <div>
              <span className="label">{t("Machine to run on")} *</span>
              {hostList.length === 0 ? <div className="rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-800">{t("No machines yet — add one on the Machines (Fleet) page first.")}</div> : (
                <div className="grid gap-2 md:grid-cols-2">
                  {hostList.map((h) => {
                    const ok = h.state === "online" || h.state === "busy";
                    const gpus = h.hostInfo?.gpus ?? [];
                    return (
                      <button key={h.id} type="button" disabled={!ok}
                        className={`rounded-lg border p-2.5 text-left transition disabled:cursor-not-allowed disabled:opacity-50 ${f.hostId === h.id ? "border-primary bg-primary/10" : "border-line hover:border-primary/40"}`}
                        onClick={() => setF({ ...f, hostId: h.id, gpus: Math.min(f.gpus, gpus.length) })}>
                        <div className="flex items-center justify-between gap-2"><span className="text-sm font-medium">{h.name}</span><StateBadge state={h.state} /></div>
                        <div className="mt-1 text-xs text-fg-muted">{gpus.length ? gpus.map((g) => g.name).join(", ") : t("no GPU")} · {h.hostInfo?.logicalCpus ?? "—"} CPU</div>
                      </button>);
                  })}
                </div>)}
            </div>
            <div className="grid gap-3 md:grid-cols-4">
              <Field label={t("Name")}><input className="input" value={f.name} placeholder={t("auto-generated if empty")} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
              <Field label="CPU"><input className="input" type="number" min={1} value={f.cpus} onChange={(e) => setF({ ...f, cpus: Number(e.target.value) })} /></Field>
              <Field label="RAM (GB)"><input className="input" type="number" min={1} value={f.memoryGb} onChange={(e) => setF({ ...f, memoryGb: Number(e.target.value) })} /></Field>
              <Field label="GPU"><input className="input" type="number" min={0} value={f.gpus} onChange={(e) => setF({ ...f, gpus: Number(e.target.value) })} /></Field>
            </div>
            <Field label={t("Auto-stop after idle")}>
              <select className="input" value={f.idleTimeoutMin} onChange={(e) => setF({ ...f, idleTimeoutMin: Number(e.target.value) })}>
                {[30, 60, 120, 240, 480].map((m) => <option key={m} value={m}>{t("{m} min", { m })}</option>)}
              </select>
            </Field>
            <ErrorBox error={create.error ? errorText(create.error) : null} />
            <div className="flex justify-end gap-2">
              <button className="btn-ghost" onClick={() => setOpen(false)}>{t("Cancel")}</button>
              <button className="btn-primary" disabled={create.isPending || !f.hostId} onClick={() => create.mutate()}>{t("Create notebook")}</button>
            </div>
          </div>
        </Dialog>)}
    </>
  );
}

/** S-08-3 Startup + IDE: progress while not running; once running, a thin bar + JupyterLab iframe via /nb/{id}/. */
export function NotebookPage({ ws, id, onBack }: { ws: string; id: string; onBack: () => void }) {
  const qc = useQueryClient();
  const nb = useQuery({ queryKey: ["notebook", ws, id], queryFn: () => api.notebook.getServer({ workspaceId: ws, serverId: id }),
    refetchInterval: (q) => (q.state.data?.state === "running" ? 30000 : 2000) });
  useCrumb(id, nb.data?.name);
  const stop = useMutation({ mutationFn: () => api.notebook.stopServer({ workspaceId: ws, serverId: id }), onSuccess: () => qc.invalidateQueries({ queryKey: ["notebook", ws, id] }) });
  const extend = useMutation({ mutationFn: () => api.notebook.extendIdle({ workspaceId: ws, serverId: id }), onSuccess: () => qc.invalidateQueries({ queryKey: ["notebook", ws, id] }) });
  const start = useMutation({ mutationFn: () => api.notebook.startServer({ workspaceId: ws, serverId: id, hostId: "" }), onSuccess: () => qc.invalidateQueries({ queryKey: ["notebook", ws, id] }) });
  const del = useMutation({ mutationFn: () => api.notebook.deleteServer({ workspaceId: ws, serverId: id }), onSuccess: () => { void qc.invalidateQueries({ queryKey: ["notebooks", ws] }); onBack(); } });
  const [confirmDel, setConfirmDel] = useState(false);
  const n = nb.data;
  if (!n) return <div className="p-6 text-fg-muted">{nb.error ? errorText(nb.error) : t("Loading…")}</div>;

  if (n.state !== "running") {
    const at = steps.indexOf(n.state);
    return (
      <>
        <button className="mb-3 text-sm text-brand-500" onClick={onBack}>← Notebooks</button>
        <PageHeader title={n.name} subtitle={<StateBadge state={n.state} />}
          actions={n.state === "stopping" ? <button className="btn-ghost text-warning" disabled={stop.isPending} onClick={() => stop.mutate()}><Square className="h-4 w-4" />{t("Force stop")}</button>
            : ["stopped", "failed"].includes(n.state) ? <>
            <button className="btn-ghost text-danger" onClick={() => setConfirmDel(true)}><Trash2 className="h-4 w-4" />{t("Delete")}</button>
            <button className="btn-primary" disabled={start.isPending} onClick={() => start.mutate()}><Play className="h-4 w-4" />{t("Start again")}</button>
          </> : undefined} />
        <ErrorBox error={start.error ? errorText(start.error) : del.error ? errorText(del.error) : stop.error ? errorText(stop.error) : null} />
        <div className="card max-w-xl space-y-2">
          {steps.map((s, i) => (
            <div key={s} className="flex items-center gap-2 text-sm">
              <span className={`h-2.5 w-2.5 rounded-full ${at > i ? "bg-emerald-500" : at === i ? "animate-pulse bg-sky-500" : "bg-slate-200"}`} />
              <span className={at >= i ? "text-slate-800" : "text-slate-400"}>{t(stepLabel[s])}</span>
            </div>))}
          {n.stateReason && <div className="pt-2 text-sm text-slate-500">{t(n.stateReason)}</div>}
          {n.state === "stopping" && <div className="pt-2 text-sm text-fg-muted">{t("Waiting for the machine to stop the container. If it does not confirm within 5 minutes the notebook is marked stopped automatically — or press Force stop now.")}</div>}
        </div>
        {confirmDel && <ConfirmDialog title={t("Delete notebook?")} label={t("Delete")} busy={del.isPending} onClose={() => setConfirmDel(false)} onConfirm={() => del.mutate()}
          consequence={t("The notebook is removed from the list. Files in your home on the machine are kept and can be used by other notebooks.")} />}
      </>
    );
  }
  return (
    <div className="fixed inset-0 z-40 flex flex-col bg-bg">
      <div className="flex h-11 items-center gap-3 border-b border-line bg-surface px-3 text-sm">
        <button className="inline-flex h-8 items-center gap-1.5 rounded-md px-2 text-fg-muted hover:bg-surface-2 hover:text-fg" onClick={onBack}><ArrowLeft className="h-4 w-4" />Notebooks</button>
        <span className="h-5 w-px bg-line" />
        <span className="font-medium">{n.name}</span>
        <span className="hidden text-xs text-fg-subtle md:inline">{n.profile} · {n.cpus} CPU · {n.memoryGb} GB{n.gpus ? ` · ${n.gpus} GPU` : ""} · {t("auto-stops after {n} min idle", { n: n.idleTimeoutMin })}</span>
        <span className="ml-auto" />
        {n.publicUrl && <a className="btn-ghost h-8 text-xs" href={n.publicUrl} target="_blank" rel="noreferrer"><ExternalLink className="h-3.5 w-3.5" />{t("Open in new tab")}</a>}
        <button className="btn-ghost h-8 text-xs" onClick={() => extend.mutate()}><Clock className="h-3.5 w-3.5" />{t("Keep 1 more hour")}</button>
        <button className="btn-ghost h-8 text-xs" onClick={() => stop.mutate()}><Square className="h-3.5 w-3.5" />{t("Stop")}</button>
      </div>
      <iframe title={n.name} src={n.url} className="min-h-0 flex-1 border-0" />
    </div>
  );
}
