import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ago, bytes } from "../lib/format";
import { ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, PageHeader, RelTime, Tabs } from "../components/ui";
import { CollectionView } from "../components/DataTable";
import { HubImportDialog, HubImportsPanel } from "../components/HuggingFace";
import { DatasetLabeling } from "./Labeling";
import { translate as t } from "../i18n";
import { Database, Download, Lock, Trash2, Upload } from "lucide-react";
import { DatasetBrowser, DatasetThumbs } from "../components/DatasetBrowser";
import { useCrumb } from "../lib/crumbs";
import { useSticky } from "../lib/persist";

/** S-04-1 Datasets + S-04-2 Create dataset. */
export function DatasetsPage({ ws, onOpen }: { ws: string; onOpen: (id: string) => void }) {
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ["datasets", ws], queryFn: () => api.data.listDatasets({ workspaceId: ws }) });
  const tasks = useQuery({ queryKey: ["tasks"], queryFn: () => api.catalog.listTasks({}) });
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ name: "", slug: "", task: "detect" });
  const create = useMutation({
    mutationFn: () => api.data.createDataset({ workspaceId: ws, ...f }),
    onSuccess: (d) => { qc.invalidateQueries({ queryKey: ["datasets", ws] }); setOpen(false); onOpen(d.id); },
  });
  const ds = list.data?.datasets ?? [];
  const [hub, setHub] = useState(false);
  return (
    <>
      <PageHeader title="Datasets"
        actions={<>
          <button className="btn-ghost" onClick={() => setHub(true)}><Download className="h-4 w-4" />{t("Import")}</button>
          <button className="btn-primary" onClick={() => setOpen(true)}>+ Dataset</button>
        </>} />
      <HubImportsPanel ws={ws} kind="dataset" onOpen={onOpen} activeOnly />
      <ErrorBox error={list.error ? errorText(list.error) : null} />
      {ds.length === 0 && !list.isLoading ? <EmptyState title={t("No datasets yet")} action={<button className="btn-primary" onClick={() => setOpen(true)}>{t("Create dataset")}</button>} /> : (
        <CollectionView rows={ds} loading={list.isLoading} rowKey={(d) => d.id} onOpen={(d) => onOpen(d.id)}
          filters={[{ key: "task", label: "Task", options: [...new Set(ds.map((d) => d.task))].map((t) => ({ value: t, label: t })), test: (d, v) => d.task === v }]}
          card={(d) => (
            <button className="card w-full text-left transition hover:border-primary/50" onClick={() => onOpen(d.id)}>
              <div className="flex items-start justify-between gap-2"><span className="font-medium">{d.name}</span><span className="badge bg-surface-2 text-fg-muted">{d.task}</span></div>
              <div className="mt-2 flex flex-wrap gap-1.5 text-xs text-fg-muted">
                <span>{t("{n} files", { n: String(d.fileCount) })}</span>·<span>{bytes(d.sizeBytes)}</span>·<span>{d.currentVersion ? `v${d.currentVersion}` : t("no version yet")}</span>
              </div>
              <DatasetThumbs ws={ws} datasetId={d.id} />
              <div className="mt-2 text-xs text-fg-subtle"><RelTime at={d.createdAt} /></div>
            </button>)}
          columns={[
            { key: "name", header: t("Name"), value: (d) => d.name, cell: (d) => <span className="font-medium">{d.name}</span> },
            { key: "task", header: "Task", value: (d) => d.task, cell: (d) => d.task },
            { key: "files", header: "File", align: "right", value: (d) => Number(d.fileCount), cell: (d) => String(d.fileCount) },
            { key: "size", header: t("Size"), align: "right", value: (d) => Number(d.sizeBytes), cell: (d) => bytes(d.sizeBytes) },
            { key: "ver", header: "Version", value: (d) => d.currentVersion, cell: (d) => (d.currentVersion ? `v${d.currentVersion}` : "—") },
            { key: "created", header: t("Created"), value: (d) => Number(d.createdAt?.seconds ?? 0), cell: (d) => <RelTime at={d.createdAt} /> },
          ]} />)}
      {hub && <HubImportDialog ws={ws} kind="dataset" onClose={() => setHub(false)} onStarted={() => void qc.invalidateQueries({ queryKey: ["datasets", ws] })} />}
      {open && (
        <Dialog title={t("Create dataset")} onClose={() => setOpen(false)}>
          <div className="space-y-3">
            <Field label={t("Name")}><input className="input" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value, slug: e.target.value.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") })} /></Field>
            <Field label={t("Path")}><input className="input" value={f.slug} onChange={(e) => setF({ ...f, slug: e.target.value })} /></Field>
            <Field label={t("Problem (task)")}>
              <select className="input" value={f.task} onChange={(e) => setF({ ...f, task: e.target.value })}>
                {(tasks.data?.tasks ?? []).map((t) => <option key={t.key} value={t.key}>{t.key} ({t.modalityIn} → {t.modalityOut})</option>)}
              </select>
            </Field>
            <ErrorBox error={create.error ? errorText(create.error) : null} />
            <button className="btn-primary" disabled={!f.name || !f.slug || create.isPending} onClick={() => create.mutate()}>{t("Created")}</button>
          </div>
        </Dialog>)}
    </>
  );
}

/** S-04-3 Dataset: content browser, versions, labeling, imports. Uploads go straight to S3 storage via presigned PUT. */
export function DatasetPage({ ws, id, onBack, onOpenLabeling }: { ws: string; id: string; onBack: () => void; onOpenLabeling: (projectId: string) => void }) {
  const qc = useQueryClient();
  const ds = useQuery({ queryKey: ["dataset", ws, id], queryFn: () => api.data.getDataset({ workspaceId: ws, datasetId: id }) });
  const files = useQuery({ queryKey: ["files", ws, id], queryFn: () => api.data.listFiles({ workspaceId: ws, datasetId: id }) });
  const versions = useQuery({ queryKey: ["versions", ws, id], queryFn: () => api.data.listVersions({ workspaceId: ws, datasetId: id }) });
  const input = useRef<HTMLInputElement>(null);
  const folderInput = useRef<HTMLInputElement>(null);
  const [progress, setProgress] = useState<{ done: number; total: number; path: string } | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [tab, setTab] = useSticky<"content" | "versions" | "labeling" | "imports">("dataset:tab", "content");
  // A link may name the tab (?tab=versions), e.g. after labels were exported into a new version.
  useEffect(() => { const v = new URLSearchParams(window.location.search).get("tab"); if (v === "content" || v === "versions" || v === "labeling" || v === "imports") setTab(v); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps
  const refresh = () => ["dataset", "files", "versions", "datasets", "dataset-browse", "dataset-stats", "dataset-thumbs"].forEach((k) => qc.invalidateQueries({ queryKey: [k, ws] }));
  useCrumb(id, ds.data?.name);

  async function upload(list: FileList) {
    setErr(null);
    try {
      let i = 0;
      for (const file of Array.from(list)) {
        const path = (file as File & { webkitRelativePath?: string }).webkitRelativePath || file.name;
        setProgress({ done: i, total: list.length, path });
        const target = await api.data.createUpload({ workspaceId: ws, datasetId: id, path, sizeBytes: BigInt(file.size) });
        const res = await fetch(target.url, { method: "PUT", body: file });
        if (!res.ok) throw new Error(t("Upload of {p} failed: HTTP {s}", { p: path, s: res.status }));
        await api.data.completeUpload({ workspaceId: ws, datasetId: id, path });
        i++;
      }
      setProgress(null);
      refresh();
    } catch (e) { setErr(errorText(e)); setProgress(null); refresh(); }
  }
  const version = useMutation({ mutationFn: () => api.data.createVersion({ workspaceId: ws, datasetId: id, note: "" }), onSuccess: () => { refresh(); setTab("content"); } });
  const removals = useQuery({ queryKey: ["dataset-removals", ws, id], queryFn: () => api.data.listPendingRemovals({ workspaceId: ws, datasetId: id }) });
  const removedSet = new Set(removals.data?.paths ?? []);
  const afterChange = () => { refresh(); void qc.invalidateQueries({ queryKey: ["dataset-removals", ws, id] }); };
  const remove = useMutation({ mutationFn: (paths: string[]) => api.data.removeFiles({ workspaceId: ws, datasetId: id, paths }), onSuccess: afterChange });
  const keep = useMutation({ mutationFn: (paths: string[]) => api.data.keepFiles({ workspaceId: ws, datasetId: id, paths }), onSuccess: afterChange });
  const [confirm, setConfirm] = useState<{ kind: "dataset" } | { kind: "version"; versionId: string; number: number } | null>(null);
  const delVersion = useMutation({ mutationFn: (versionId: string) => api.data.deleteVersion({ workspaceId: ws, versionId }), onSuccess: () => { setConfirm(null); refresh(); } });
  const delDataset = useMutation({ mutationFn: () => api.data.deleteDataset({ workspaceId: ws, datasetId: id }), onSuccess: () => { setConfirm(null); void qc.invalidateQueries({ queryKey: ["datasets", ws] }); onBack(); } });
  const [importing, setImporting] = useState(false);
  const d = ds.data;
  const vs = [...(versions.data?.versions ?? [])].sort((a, b) => b.number - a.number);
  const working = files.data?.files.length ?? 0;
  const pending = working + removedSet.size;
  return (
    <>
      <button className="mb-3 text-sm text-primary hover:underline" onClick={onBack}>← Datasets</button>
      <PageHeader title={d?.name ?? "Dataset"} icon={<Database className="h-5 w-5" />}
        subtitle={d && <span>{t("task")} <b>{d.task}</b> · {vs.length > 0 ? t("{n} versions", { n: vs.length }) : t("no version yet")}{working > 0 && <> · {t("{n} new files", { n: working })}</>}{removedSet.size > 0 && <> · {t("{n} files to remove", { n: removedSet.size })}</>}</span>}
        actions={<>
          <input ref={input} type="file" multiple hidden onChange={(e) => { if (e.target.files) void upload(e.target.files); e.target.value = ""; }} />
          <input ref={folderInput} type="file" multiple hidden {...({ webkitdirectory: "" } as Record<string, string>)} onChange={(e) => { if (e.target.files) void upload(e.target.files); e.target.value = ""; }} />
          <button className="btn-ghost" onClick={() => setImporting(true)}><Download className="h-4 w-4" />{t("Import version")}</button>
          <button className="btn-ghost" onClick={() => input.current?.click()} disabled={!!progress}><Upload className="h-4 w-4" />{t("Add files")}</button>
          <button className="btn-ghost" onClick={() => folderInput.current?.click()} disabled={!!progress}><Upload className="h-4 w-4" />{t("Add folder")}</button>
          <button className="btn-primary" onClick={() => version.mutate()} disabled={version.isPending || pending === 0}>
            <Lock className="h-4 w-4" />{t("Freeze v{n}", { n: (d?.currentVersion ?? 0) + 1 })}</button>
          <button className="btn-ghost px-2 text-danger" aria-label={t("Delete dataset")} title={t("Delete dataset")} onClick={() => setConfirm({ kind: "dataset" })}><Trash2 className="h-4 w-4" /></button>
        </>} />
      {progress && (
        <div className="mb-3 rounded-lg border border-line bg-surface p-3 text-sm">
          <div className="mb-1.5 flex justify-between gap-3"><span className="truncate">{t("Uploading {i}/{n}: {p}", { i: progress.done + 1, n: progress.total, p: progress.path })}</span>
            <span className="mono text-xs text-fg-muted">{Math.round((100 * progress.done) / progress.total)}%</span></div>
          <div className="h-1.5 overflow-hidden rounded-full bg-surface-2"><div className="h-full bg-primary transition-all" style={{ width: `${(100 * progress.done) / progress.total}%` }} /></div>
        </div>)}
      {importing && d && <HubImportDialog ws={ws} kind="dataset" datasetId={id} defaultTask={d.task} onClose={() => setImporting(false)} />}
      <ErrorBox error={err ?? ([version.error, remove.error, keep.error, ds.error].filter(Boolean).map((e) => errorText(e))[0] ?? null)} />
      <HubImportsPanel ws={ws} kind="dataset" targetId={id} activeOnly />
      <Tabs value={tab} onChange={setTab} tabs={[
        { key: "content", label: t("Content") }, { key: "versions", label: "Versions", count: vs.length },
        { key: "labeling", label: t("Labeling") }, { key: "imports", label: t("Imports") },
      ]} />
      <div className="mt-4">
        {tab === "content" && (versions.isLoading || files.isLoading ? null : (
          <DatasetBrowser ws={ws} datasetId={id} workingFiles={working} removed={removedSet} busy={remove.isPending || keep.isPending}
            onRemove={(paths) => remove.mutate(paths)} onKeep={(paths) => keep.mutate(paths)}
            versions={vs.map((v) => ({ id: v.id, label: `v${v.number} · ${t("{n} files", { n: String(v.fileCount) })}${v.storage === "reference" ? " · " + t("reference") : ""}` }))} />))}
        {tab === "versions" && (
          <div className="space-y-3">
            {pending > 0 && (
              <div className="flex items-center justify-between gap-3 rounded-lg border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm">
                <span>v{(d?.currentVersion ?? 0) + 1}: {[working > 0 ? t("+{n} files", { n: working }) : "", removedSet.size > 0 ? t("−{n} files", { n: removedSet.size }) : ""].filter(Boolean).join(" · ")}</span>
                <button className="btn-primary h-8" onClick={() => version.mutate()} disabled={version.isPending}><Lock className="h-4 w-4" />{t("Freeze v{n}", { n: (d?.currentVersion ?? 0) + 1 })}</button>
              </div>)}
            {vs.length === 0 ? <EmptyState bare title={t("No version yet — training needs one.")} /> : (
              <div className="overflow-hidden rounded-lg border border-line">
                <table className="w-full text-sm">
                  <thead className="bg-surface-2 text-left text-xs text-fg-muted"><tr><th className="px-3 py-2">Version</th><th>{t("Storage")}</th><th>{t("Files")}</th><th>{t("Size")}</th><th>{t("Source")}</th><th className="pr-3 text-right">{t("Created")}</th><th className="w-10" /></tr></thead>
                  <tbody>
                    {vs.map((v) => (
                      <tr key={v.id} className="border-t border-line">
                        <td className="px-3 py-2 font-medium">v{v.number}</td>
                        <td><span className={`badge ${v.storage === "reference" ? "bg-sky-100 text-sky-700" : "bg-surface-2 text-fg-muted"}`}>
                          {v.storage === "reference" ? t("reference") : t("in storage")}</span></td>
                        <td className="mono text-xs">{String(v.fileCount)}</td>
                        <td className="mono text-xs">{bytes(v.sizeBytes)}</td>
                        <td className="mono max-w-md truncate text-xs text-fg-subtle" title={v.sourceRef}>{v.sourceRef || "—"}</td>
                        <td className="pr-3 text-right text-xs text-fg-subtle">{ago(v.createdAt)}</td>
                        <td className="pr-2"><button className="btn-ghost h-8 px-2 text-danger" aria-label={t("Delete v{n}", { n: v.number })} title={t("Delete v{n}", { n: v.number })}
                          onClick={() => setConfirm({ kind: "version", versionId: v.id, number: v.number })}><Trash2 className="h-4 w-4" /></button></td>
                      </tr>))}
                  </tbody>
                </table>
              </div>)}
          </div>)}
        {tab === "labeling" && <DatasetLabeling ws={ws} datasetId={id} onOpenProject={onOpenLabeling} />}
        {tab === "imports" && <ImportsTab ws={ws} id={id} />}
      </div>
      {confirm?.kind === "dataset" && d && (
        <ConfirmDialog title={t("Delete dataset {name}?", { name: d.name })} label="Delete" confirmText={d.slug} busy={delDataset.isPending}
          onClose={() => { setConfirm(null); delDataset.reset(); }} onConfirm={() => delDataset.mutate()}
          consequence={delDataset.error ? <ErrorBox error={errorText(delDataset.error)} /> : null} />)}
      {confirm?.kind === "version" && (
        <ConfirmDialog title={t("Delete version v{n}?", { n: confirm.number })} label="Delete" busy={delVersion.isPending}
          onClose={() => { setConfirm(null); delVersion.reset(); }} onConfirm={() => delVersion.mutate(confirm.versionId)}
          consequence={delVersion.error ? <ErrorBox error={errorText(delVersion.error)} /> : null} />)}
    </>
  );
}

function ImportsTab({ ws, id }: { ws: string; id: string }) {
  const q = useQuery({ queryKey: ["hub-imports", ws], queryFn: () => api.hub.listHubImports({ workspaceId: ws }) });
  const mine = (q.data?.imports ?? []).filter((i) => i.targetId === id);
  if (mine.length === 0 && !q.isLoading) return <EmptyState bare title={t("Nothing was imported into this dataset.")} />;
  return <HubImportsPanel ws={ws} kind="dataset" targetId={id} />;
}
