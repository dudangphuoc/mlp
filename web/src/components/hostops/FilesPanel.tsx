import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowUp, Download, File, Folder, FolderPlus, Pencil, Trash2, Upload } from "lucide-react";
import { api, errorText } from "../../lib/api";
import type { FsEntry } from "../../gen/mlplatform/v1/agent_pb";
import { bytes } from "../../lib/format";
import { ConfirmDialog, EmptyState, ErrorBox, RelTime, Skeleton } from "../ui";
import { FileEditor } from "./FileEditor";
import { translate as t } from "../../i18n";

const sep = (p: string) => (p.includes("\\") && !p.includes("/") ? "\\" : "/");
const join = (dir: string, name: string) => (dir.endsWith(sep(dir)) ? dir + name : dir + sep(dir) + name);
const parent = (p: string, roots: string[]) => {
  if (roots.includes(p)) return "";
  const s = sep(p);
  const i = p.replace(/[\\/]+$/, "").lastIndexOf(s);
  return i <= 0 ? s : p.slice(0, i);
};

/** Browse, edit, upload, download and delete files inside the machine's allowed folders. */
export function FilesPanel({ ws, hostId }: { ws: string; hostId: string }) {
  const qc = useQueryClient();
  const [path, setPath] = useState("");
  const [editing, setEditing] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<FsEntry | null>(null);
  const [renaming, setRenaming] = useState<{ from: string; to: string } | null>(null);
  const [newDir, setNewDir] = useState<string | null>(null);
  const [uploading, setUploading] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const roots = useQuery({ queryKey: ["host-files", ws, hostId, ""], queryFn: () => api.fleet.listHostFiles({ workspaceId: ws, hostId, path: "" }) });
  const list = useQuery({ queryKey: ["host-files", ws, hostId, path], queryFn: () => api.fleet.listHostFiles({ workspaceId: ws, hostId, path }) });
  const refresh = () => qc.invalidateQueries({ queryKey: ["host-files", ws, hostId, path] });
  const rootPaths = (roots.data?.entries ?? []).map((e) => e.name);
  const run = useMutation({ mutationFn: async (f: () => Promise<unknown>) => f(), onSuccess: () => { setErr(null); void refresh(); }, onError: (e) => setErr(errorText(e)) });

  const upload = async (files: FileList | null) => {
    for (const f of Array.from(files ?? [])) {
      setUploading(f.name);
      const q = new URLSearchParams({ workspace_id: ws, path: join(path, f.name) });
      const r = await fetch(`/api/hosts/${hostId}/files?${q}`, { method: "PUT", body: f, credentials: "include" });
      if (!r.ok) setErr(`${f.name}: ${r.status} ${r.headers.get("x-mlp-reason") ?? ""}`);
    }
    setUploading(null);
    void refresh();
  };
  const href = (name: string) => `/api/hosts/${hostId}/files?${new URLSearchParams({ workspace_id: ws, path: join(path, name) })}`;
  const entries = list.data?.entries ?? [];

  return (
    <div className="space-y-3" onDragOver={(e) => e.preventDefault()} onDrop={(e) => { e.preventDefault(); if (path) void upload(e.dataTransfer.files); }}>
      <div className="flex flex-wrap items-center gap-2">
        <button className="btn-ghost h-8 px-2" disabled={!path} onClick={() => setPath(parent(path, rootPaths))}><ArrowUp className="h-4 w-4" /></button>
        <span className="mono min-w-0 flex-1 truncate text-xs">{path || t("Allowed folders")}</span>
        {path && <>
          <button className="btn-ghost h-8 px-2 text-xs" onClick={() => setNewDir("")}><FolderPlus className="h-4 w-4" />{t("New folder")}</button>
          <button className="btn-ghost h-8 px-2 text-xs" disabled={uploading !== null} onClick={() => fileInput.current?.click()}><Upload className="h-4 w-4" />{uploading ?? t("Upload")}</button>
          <input ref={fileInput} type="file" multiple hidden onChange={(e) => void upload(e.target.files)} />
        </>}
      </div>
      <ErrorBox error={err ?? (list.error ? errorText(list.error) : null)} />
      {newDir !== null && (
        <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); run.mutate(() => api.fleet.makeHostDir({ workspaceId: ws, hostId, path: join(path, newDir) })); setNewDir(null); }}>
          <input className="input" value={newDir} onChange={(e) => setNewDir(e.target.value)} autoFocus placeholder={t("Folder name")} />
          <button className="btn-primary" disabled={!newDir.trim()}>{t("Create")}</button>
          <button type="button" className="btn-ghost" onClick={() => setNewDir(null)}>{t("Cancel")}</button>
        </form>)}
      {list.isLoading ? <Skeleton rows={6} /> : entries.length === 0 ? <EmptyState bare title={t("Empty folder")} /> : (
        <div className="card overflow-x-auto p-0">
          <table className="w-full text-sm">
            <thead className="text-left text-xs text-fg-subtle"><tr><th className="px-3 py-2">{t("Name")}</th><th className="px-3 py-2 text-right">{t("Size")}</th><th className="px-3 py-2">{t("Modified")}</th><th /></tr></thead>
            <tbody>{entries.map((e) => {
              const full = path ? join(path, e.name) : e.name;
              return (
                <tr key={e.name} className="group border-t border-line hover:bg-surface-2">
                  <td className="px-3 py-1.5">
                    {renaming?.from === full ? (
                      <form className="flex gap-2" onSubmit={(ev) => { ev.preventDefault(); run.mutate(() => api.fleet.moveHostPath({ workspaceId: ws, hostId, from: full, to: join(path, renaming.to) })); setRenaming(null); }}>
                        <input className="input h-7" value={renaming.to} onChange={(ev) => setRenaming({ from: full, to: ev.target.value })} autoFocus />
                        <button className="btn-primary h-7 px-2 text-xs">{t("Rename")}</button>
                      </form>
                    ) : (
                      <button className="flex items-center gap-2 text-left" onClick={() => (e.isDir ? setPath(full) : setEditing(full))}>
                        {e.isDir ? <Folder className="h-4 w-4 text-accent" /> : <File className="h-4 w-4 text-fg-subtle" />}
                        <span className={path ? "" : "mono text-xs"}>{e.name}</span>
                      </button>)}
                  </td>
                  <td className="px-3 py-1.5 text-right tabular-nums text-fg-muted">{e.isDir ? "" : bytes(e.size)}</td>
                  <td className="px-3 py-1.5 text-xs text-fg-muted"><RelTime at={e.modifiedAt} /></td>
                  <td className="px-3 py-1.5 text-right">{path && (
                    <span className="invisible inline-flex gap-1 group-hover:visible">
                      {!e.isDir && <a className="btn-ghost h-7 px-2" href={href(e.name)} download><Download className="h-3.5 w-3.5" /></a>}
                      {!e.isDir && <button className="btn-ghost h-7 px-2" onClick={() => setEditing(full)}><Pencil className="h-3.5 w-3.5" /></button>}
                      <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setRenaming({ from: full, to: e.name })}>{t("Rename")}</button>
                      <button className="btn-ghost h-7 px-2 text-danger" onClick={() => setDeleting(e)}><Trash2 className="h-3.5 w-3.5" /></button>
                    </span>)}
                  </td>
                </tr>);
            })}</tbody>
          </table>
          {list.data?.truncated && <div className="border-t border-line px-3 py-2 text-xs text-fg-muted">{t("First {n} entries", { n: entries.length })}</div>}
        </div>)}
      {editing && <FileEditor ws={ws} hostId={hostId} path={editing} onClose={() => { setEditing(null); void refresh(); }} />}
      {deleting && <ConfirmDialog title={t("Delete {name}", { name: deleting.name })} consequence={<span className="mono text-xs">{join(path, deleting.name)}</span>}
        confirmText={deleting.isDir ? deleting.name : undefined} label="Delete" busy={run.isPending}
        onConfirm={() => { run.mutate(() => api.fleet.deleteHostPath({ workspaceId: ws, hostId, path: join(path, deleting.name), recursive: deleting.isDir })); setDeleting(null); }}
        onClose={() => setDeleting(null)} />}
    </div>
  );
}
