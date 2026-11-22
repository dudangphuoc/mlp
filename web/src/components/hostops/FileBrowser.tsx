import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, FolderPlus, RefreshCw, Upload } from "lucide-react";
import { api, errorText } from "../../lib/api";
import type { FsEntry } from "../../gen/mlplatform/v1/agent_pb";
import { bytes, dateTime } from "../../lib/format";
import { ConfirmDialog, ErrorBox } from "../ui";
import { translate as t } from "../../i18n";

const sepOf = (p: string) => (/^[A-Za-z]:\\/.test(p) ? "\\" : "/");
export const joinPath = (dir: string, name: string) => (dir.endsWith(sepOf(dir)) ? dir + name : dir + sepOf(dir) + name);
const parentOf = (p: string) => {
  const s = sepOf(p);
  const trimmed = p.length > 3 ? p.replace(/[\\/]+$/, "") : p;
  const i = trimmed.lastIndexOf(s);
  return i < 0 ? p : i === 0 ? s : trimmed.slice(0, i + (i === 2 && s === "\\" ? 1 : 0)) || s;
};
const driveOf = (p: string, drives: string[]) => drives.filter((d) => p.toLowerCase().startsWith(d.toLowerCase())).sort((a, b) => b.length - a.length)[0] ?? drives[0] ?? "";

const btn = "inline-flex h-8 flex-none items-center gap-1.5 whitespace-nowrap rounded-md border border-line px-3 text-sm hover:bg-surface-2 disabled:opacity-50";
const icon = "inline-flex h-8 w-8 flex-none items-center justify-center rounded-md border border-line hover:bg-surface-2 disabled:opacity-50";
const small = "inline-flex h-6 flex-none items-center whitespace-nowrap rounded border border-line px-2 text-xs hover:bg-surface-2 disabled:opacity-50";

function TreeNode({ ws, hostId, path, label, depth, current, onOpen }:
  { ws: string; hostId: string; path: string; label: string; depth: number; current: string; onOpen: (p: string) => void }) {
  const [open, setOpen] = useState(current.toLowerCase().startsWith(path.toLowerCase()));
  const q = useQuery({ queryKey: ["host-files", ws, hostId, path], queryFn: () => api.fleet.listHostFiles({ workspaceId: ws, hostId, path }), enabled: open });
  const dirs = (q.data?.entries ?? []).filter((e) => e.isDir);
  const active = current === path;
  return (
    <div>
      <div className={`flex h-8 items-center gap-1 whitespace-nowrap pr-2 ${active ? "bg-surface-2 text-fg" : "text-fg-muted hover:text-fg"}`} style={{ paddingLeft: 8 + depth * 12 }}>
        <button className="inline-flex h-5 w-5 flex-none items-center justify-center" aria-label={open ? t("Collapse") : t("Expand")} onClick={() => setOpen(!open)}>
          {open ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
        </button>
        <button className="mono min-w-0 truncate text-left text-xs" onClick={() => { setOpen(true); onOpen(path); }}>{label}</button>
      </div>
      {open && dirs.slice(0, 300).map((d) => (
        <TreeNode key={d.name} ws={ws} hostId={hostId} path={joinPath(path, d.name)} label={d.name} depth={depth + 1} current={current} onOpen={onOpen} />))}
    </div>
  );
}

/** Whole-drive file browser: drive + path bar, folder tree, listing, actions on the selected entry. */
export function FileBrowser({ ws, hostId, onOpenFile, compact }: { ws: string; hostId: string; onOpenFile: (path: string) => void; compact?: boolean }) {
  const qc = useQueryClient();
  const drivesQ = useQuery({ queryKey: ["host-files", ws, hostId, ""], queryFn: () => api.fleet.listHostFiles({ workspaceId: ws, hostId, path: "" }) });
  const drives = (drivesQ.data?.entries ?? []).map((e) => e.name);
  const [path, setPath] = useState("");
  const [typed, setTyped] = useState<string | null>(null);
  const [picked, setPicked] = useState<FsEntry | null>(null);
  const [mode, setMode] = useState<"none" | "mkdir" | "rename">("none");
  const [name, setName] = useState("");
  const [del, setDel] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const cwd = path || drives[0] || "";
  const list = useQuery({ queryKey: ["host-files", ws, hostId, cwd], queryFn: () => api.fleet.listHostFiles({ workspaceId: ws, hostId, path: cwd }), enabled: cwd !== "" });
  const refresh = () => qc.invalidateQueries({ queryKey: ["host-files", ws, hostId, cwd] });
  const run = useMutation({ mutationFn: (f: () => Promise<unknown>) => f(), onSuccess: () => { setErr(null); setPicked(null); void refresh(); }, onError: (e) => setErr(errorText(e)) });
  const go = (p: string) => { setPath(p); setTyped(null); setPicked(null); setMode("none"); };
  const entries = list.data?.entries ?? [];
  const full = picked ? joinPath(cwd, picked.name) : "";

  const upload = async (files: FileList | null) => {
    for (const f of Array.from(files ?? [])) {
      setBusy(f.name);
      const q = new URLSearchParams({ workspace_id: ws, path: joinPath(cwd, f.name) });
      const r = await fetch(`/api/hosts/${hostId}/files?${q}`, { method: "PUT", body: f, credentials: "include" });
      if (!r.ok) setErr(`${f.name}: ${r.status} ${r.headers.get("x-mlp-reason") ?? ""}`);
    }
    setBusy(null);
    void refresh();
  };
  const submitName = () => {
    const target = joinPath(cwd, name.trim());
    run.mutate(mode === "mkdir" ? () => api.fleet.makeHostDir({ workspaceId: ws, hostId, path: target })
      : () => api.fleet.moveHostPath({ workspaceId: ws, hostId, from: full, to: target }));
    setMode("none");
  };
  const download = () => window.open(`/api/hosts/${hostId}/files?${new URLSearchParams({ workspace_id: ws, path: full })}`, "_blank");

  return (
    <div className="flex h-full min-h-0 flex-col" onDragOver={(e) => e.preventDefault()} onDrop={(e) => { e.preventDefault(); void upload(e.dataTransfer.files); }}>
      <div className="flex h-12 flex-none items-center gap-2 border-b border-line px-2">
        <label className="sr-only" htmlFor="fb-drive">{t("Drive")}</label>
        <select id="fb-drive" className="input h-8 w-20 flex-none px-2" value={driveOf(cwd, drives)} onChange={(e) => go(e.target.value)}>
          {drives.map((d) => <option key={d} value={d}>{d}</option>)}
        </select>
        <label className="sr-only" htmlFor="fb-path">{t("Path")}</label>
        <input id="fb-path" className="input mono h-8 min-w-0 flex-1 px-2 text-xs" value={typed ?? cwd} onChange={(e) => setTyped(e.target.value)}
          onKeyDown={(e) => { if (e.key === "Enter" && typed) go(typed); }} />
        <button className={btn} aria-label={t("Upload")} disabled={busy !== null} onClick={() => fileInput.current?.click()}><Upload className="h-4 w-4" />{compact ? "" : busy ?? t("Upload")}</button>
        <input ref={fileInput} type="file" multiple hidden onChange={(e) => void upload(e.target.files)} />
        <button className={icon} aria-label={t("New folder")} onClick={() => { setMode("mkdir"); setName(""); }}><FolderPlus className="h-4 w-4" /></button>
        <button className={icon} aria-label={t("Refresh")} onClick={() => void refresh()}><RefreshCw className="h-4 w-4" /></button>
      </div>
      <ErrorBox error={err ?? (list.error ? errorText(list.error) : drivesQ.error ? errorText(drivesQ.error) : null)} />
      {mode !== "none" && (
        <form className="flex h-12 flex-none items-center gap-2 border-b border-line px-2" onSubmit={(e) => { e.preventDefault(); if (name.trim()) submitName(); }}>
          <label className="sr-only" htmlFor="fb-name">{t("Name")}</label>
          <input id="fb-name" className="input h-8 min-w-0 flex-1 px-2" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          <button className={`${btn} border-primary bg-primary text-white`} disabled={!name.trim()}>{mode === "mkdir" ? t("Create") : t("Rename")}</button>
          <button type="button" className={btn} onClick={() => setMode("none")}>{t("Cancel")}</button>
        </form>)}
      <div className={`grid min-h-0 flex-1 ${compact ? "grid-cols-1" : "grid-cols-[200px_minmax(0,1fr)]"}`}>
        {!compact && (
          <div className="min-h-0 overflow-auto border-r border-line py-1">
            {drives.map((d) => <TreeNode key={d} ws={ws} hostId={hostId} path={d} label={d} depth={0} current={cwd} onOpen={go} />)}
          </div>)}
        <div className="min-h-0 overflow-auto">
          <table className="w-full table-fixed text-sm">
            <thead className="sticky top-0 bg-surface text-left text-xs text-fg-subtle">
              <tr className="h-8"><th className="px-3 font-normal">{t("Name")}</th><th className="w-24 px-3 text-right font-normal">{t("Size")}</th>
                {!compact && <th className="w-40 px-3 font-normal">{t("Modified")}</th>}</tr>
            </thead>
            <tbody>
              {cwd !== "" && !drives.includes(cwd) && (
                <tr className="h-8 cursor-pointer hover:bg-surface-2" onClick={() => go(parentOf(cwd))}><td className="px-3 text-primary" colSpan={3}>..</td></tr>)}
              {entries.map((e) => (
                <tr key={e.name} className={`h-8 cursor-pointer ${picked?.name === e.name ? "bg-primary/15" : "hover:bg-surface-2"}`}
                  onClick={() => setPicked(e)} onDoubleClick={() => (e.isDir ? go(joinPath(cwd, e.name)) : onOpenFile(joinPath(cwd, e.name)))}>
                  <td className={`truncate px-3 ${e.isDir ? "text-primary" : ""}`}>{e.name}{e.isDir ? "/" : ""}</td>
                  <td className="whitespace-nowrap px-3 text-right tabular-nums text-fg-muted">{e.isDir ? "" : bytes(e.size)}</td>
                  {!compact && <td className="truncate whitespace-nowrap px-3 text-xs tabular-nums text-fg-muted">{dateTime(e.modifiedAt)}</td>}
                </tr>))}
            </tbody>
          </table>
        </div>
      </div>
      <div className="flex h-10 flex-none items-center gap-2 border-t border-line px-3 text-xs text-fg-muted">
        <span className="whitespace-nowrap">{entries.length}{list.data?.truncated ? "+" : ""}</span>
        <span className="mono min-w-0 flex-1 truncate">{picked?.name ?? ""}</span>
        <button className={small} disabled={!picked} onClick={() => picked && (picked.isDir ? go(full) : onOpenFile(full))}>{t("Open")}</button>
        <button className={small} disabled={!picked || picked.isDir} onClick={download}>{t("Download")}</button>
        <button className={small} disabled={!picked} onClick={() => { setMode("rename"); setName(picked?.name ?? ""); }}>{t("Rename")}</button>
        <button className={`${small} text-danger`} disabled={!picked} onClick={() => setDel(true)}>{t("Delete")}</button>
      </div>
      {del && picked && <ConfirmDialog title={t("Delete {name}", { name: picked.name })} consequence={<span className="mono break-all text-xs">{full}</span>}
        confirmText={picked.isDir ? picked.name : undefined} label="Delete" busy={run.isPending}
        onConfirm={() => { run.mutate(() => api.fleet.deleteHostPath({ workspaceId: ws, hostId, path: full, recursive: picked.isDir })); setDel(false); }}
        onClose={() => setDel(false)} />}
    </div>
  );
}
