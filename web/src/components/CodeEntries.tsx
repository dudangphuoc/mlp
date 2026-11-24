import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Play, Plus } from "lucide-react";
import { api, errorText } from "../lib/api";
import { ConfirmDialog, Dialog, EmptyState, ErrorBox, Field } from "./ui";
import { DataTable } from "./DataTable";
import { translate as t } from "../i18n";
import type { CodeEntry } from "../gen/mlplatform/v1/tracking_pb";

/** Repository scripts of a project that start from the web: which file, from which branch, on which machine. */
export function CodeEntries({ ws, projectId, onRun }: { ws: string; projectId: string; onRun: (runId: string) => void }) {
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ["code-entries", ws, projectId], queryFn: () => api.tracking.listCodeEntries({ workspaceId: ws, projectId }) });
  const [adding, setAdding] = useState(false);
  const [running, setRunning] = useState<CodeEntry | null>(null);
  const [removing, setRemoving] = useState<CodeEntry | null>(null);
  const refresh = () => void qc.invalidateQueries({ queryKey: ["code-entries", ws, projectId] });
  const remove = useMutation({ mutationFn: (id: string) => api.tracking.deleteCodeEntry({ workspaceId: ws, id }), onSuccess: () => { setRemoving(null); refresh(); } });
  const rows = list.data?.entries ?? [];
  return (
    <>
      <div className="mb-3 flex justify-end"><button className="btn-primary" onClick={() => setAdding(true)}><Plus className="h-4 w-4" />{t("Add script")}</button></div>
      <ErrorBox error={list.error ? errorText(list.error) : null} />
      {!list.isLoading && rows.length === 0 ? <EmptyState title={t("No repository scripts yet")} /> : (
        <DataTable rows={rows} loading={list.isLoading} rowKey={(e) => e.id} search={false}
          columns={[
            { key: "name", header: t("Name"), value: (e) => e.name, cell: (e) => <span className="font-medium">{e.name}</span> },
            { key: "repo", header: "Repo", value: (e) => e.gitRemote, cell: (e) => <span className="mono text-xs">{e.gitRemote.replace("https://", "")}</span> },
            { key: "ref", header: t("Branch"), value: (e) => e.ref, cell: (e) => <span className="mono text-xs">{e.ref}</span> },
            { key: "script", header: t("Script"), value: (e) => e.script, cell: (e) => <span className="mono text-xs">{e.script}</span> },
            { key: "machine", header: t("Machine"), value: (e) => e.machine, cell: (e) => <span className="text-xs">{e.machine} · {e.gpus} GPU</span> },
            { key: "a", header: "", align: "right", cell: (e) => (
              <span className="inline-flex gap-2 whitespace-nowrap">
                <button className="btn-primary h-7 px-2 text-xs" onClick={() => setRunning(e)}><Play className="h-3.5 w-3.5" />{t("Run")}</button>
                <button className="text-xs text-danger hover:underline" onClick={() => setRemoving(e)}>{t("Delete")}</button>
              </span>) },
          ]} />)}
      {adding && <AddEntrySheet ws={ws} projectId={projectId} onClose={() => setAdding(false)} onAdded={() => { setAdding(false); refresh(); }} />}
      {running && <RunEntrySheet ws={ws} entry={running} onClose={() => setRunning(null)} onStarted={(id) => { setRunning(null); onRun(id); }} />}
      {removing && <ConfirmDialog title={t("Delete {name}?", { name: removing.name })} label={t("Delete")} busy={remove.isPending}
        consequence={t("Past runs stay.")} onClose={() => setRemoving(null)} onConfirm={() => remove.mutate(removing.id)} />}
    </>
  );
}

function useMachines(ws: string) {
  const hosts = useQuery({ queryKey: ["hosts", ws], queryFn: () => api.fleet.listHosts({ workspaceId: ws }) });
  return ["auto", ...(hosts.data?.hosts ?? []).map((h) => h.name)];
}

function AddEntrySheet({ ws, projectId, onClose, onAdded }: { ws: string; projectId: string; onClose: () => void; onAdded: () => void }) {
  const machines = useMachines(ws);
  const [f, setF] = useState({ name: "", gitRemote: "", ref: "main", script: "train.py", machine: "auto", gpus: 1, image: "" });
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: k === "gpus" ? Number(e.target.value) : e.target.value });
  const add = useMutation({ mutationFn: () => api.tracking.createCodeEntry({ workspaceId: ws, projectId, ...f }), onSuccess: onAdded });
  return (
    <Dialog title={t("Add script")} onClose={onClose}>
      <div className="space-y-3 text-sm">
        <Field label={t("Name")}><input className="input" value={f.name} onChange={set("name")} /></Field>
        <Field label="Repo"><input className="input mono" placeholder="https://github.com/org/repo" value={f.gitRemote} onChange={set("gitRemote")} /></Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label={t("Branch")}><input className="input mono" value={f.ref} onChange={set("ref")} /></Field>
          <Field label={t("Script")}><input className="input mono" value={f.script} onChange={set("script")} /></Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label={t("Machine")}><select className="input" value={f.machine} onChange={set("machine")}>{machines.map((m) => <option key={m} value={m}>{m}</option>)}</select></Field>
          <Field label="GPU"><input className="input" type="number" min={0} value={f.gpus} onChange={set("gpus")} /></Field>
        </div>
        <Field label={t("Base image")}><input className="input mono" placeholder="python:3.12-slim" value={f.image} onChange={set("image")} /></Field>
        <ErrorBox error={add.error ? errorText(add.error) : null} />
        <div className="flex justify-end gap-2">
          <button className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-primary" disabled={!f.name.trim() || !f.gitRemote.trim() || !f.script.trim() || add.isPending} onClick={() => add.mutate()}>{t("Add script")}</button>
        </div>
      </div>
    </Dialog>
  );
}

function RunEntrySheet({ ws, entry, onClose, onStarted }: { ws: string; entry: CodeEntry; onClose: () => void; onStarted: (runId: string) => void }) {
  const machines = useMachines(ws);
  const [ref, setRef] = useState(entry.ref);
  const [machine, setMachine] = useState(entry.machine);
  const [params, setParams] = useState("");
  const parsed = Object.fromEntries(params.split("\n").map((l) => l.trim()).filter((l) => l.includes("=")).map((l) => [l.slice(0, l.indexOf("=")).trim(), l.slice(l.indexOf("=") + 1).trim()]));
  const run = useMutation({ mutationFn: () => api.tracking.runCodeEntry({ workspaceId: ws, entryId: entry.id, ref, machine, params: parsed }), onSuccess: (r) => onStarted(r.id) });
  return (
    <Dialog title={t("Run {name}", { name: entry.name })} onClose={onClose}>
      <div className="space-y-3 text-sm">
        <div className="mono text-xs text-fg-muted">{entry.gitRemote.replace("https://", "")} · {entry.script}</div>
        <div className="grid grid-cols-2 gap-3">
          <Field label={t("Branch, tag or commit")}><input className="input mono" value={ref} onChange={(e) => setRef(e.target.value)} /></Field>
          <Field label={t("Machine")}><select className="input" value={machine} onChange={(e) => setMachine(e.target.value)}>{machines.map((m) => <option key={m} value={m}>{m}</option>)}</select></Field>
        </div>
        <Field label={t("Parameters")}><textarea className="input mono h-28" placeholder={"epochs=30\nlr=0.001"} value={params} onChange={(e) => setParams(e.target.value)} /></Field>
        <ErrorBox error={run.error ? errorText(run.error) : null} />
        <div className="flex justify-end gap-2">
          <button className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-primary" disabled={!ref.trim() || run.isPending} onClick={() => run.mutate()}><Play className="h-4 w-4" />{run.isPending ? t("Fetching the code…") : t("Run")}</button>
        </div>
      </div>
    </Dialog>
  );
}
