import { useEffect, useState } from "react";
import { ArrowRight, CheckCheck, Database, FolderSync, PackageCheck, Pencil, Plus, Trash2 } from "lucide-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ago } from "../lib/format";
import { ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, PageHeader, RelTime, StateBadge, Tabs } from "../components/ui";
import { ShapeOverlay, type Shape } from "../components/DatasetBrowser";
import type { LabelingProject } from "../gen/mlplatform/v1/labeling_pb";
import { HermesProjectTab } from "../components/Hermes";
import { AssistProjectTab } from "../components/Assist";
import { QualityProjectTab } from "../components/Quality";

import { translate as t } from "../i18n";
import { useCrumb } from "../lib/crumbs";

type Cls = { id: number; name: string; color: string };
const palette = ["#e5484d", "#3e63dd", "#30a46c", "#f76b15", "#8e4ec6", "#12a594", "#d6409f", "#ffc53d", "#0090ff"];
const parseClasses = (json: string): Cls[] => {
  try { return (JSON.parse(json) as Cls[]).map((c, i) => ({ ...c, color: c.color || palette[i % palette.length] })); } catch { return []; }
};
const statusOrder = ["unlabeled", "in_progress", "in_review", "rejected", "skipped", "accepted"];
const statusLabel: Record<string, string> = {
  unlabeled: "unlabeled", in_progress: "in progress", in_review: "in review", rejected: "returned", skipped: "skipped", accepted: "accepted",
};

/** Class names from the label files that came with a version (YOLO data.yaml `names`, or classes.txt). */
function parseNames(text: string, file: string): string[] {
  if (file.endsWith(".txt")) return text.split(/\r?\n/).map((x) => x.trim()).filter(Boolean);
  const inline = /^names:\s*\[(.*)\]/m.exec(text);
  if (inline) return inline[1].split(",").map((x) => x.trim().replace(/^['"]|['"]$/g, "")).filter(Boolean);
  const block = /^names:\s*\n((?:[ \t]+.+\n?)+)/m.exec(text);
  if (!block) return [];
  return block[1].split("\n").map((l) => l.trim()).filter(Boolean).map((l) => l.replace(/^-\s*/, "").replace(/^\d+\s*:\s*/, "").replace(/^['"]|['"]$/g, ""));
}

/**
 * New labeling project: dataset, version (latest by default), classes when the dataset has none yet (read from its data.yaml when there is
 * one). The creator can label right away — the project starts active with every image of the version as an item.
 */
export function NewLabelingProjectSheet({ ws, datasetId: fixed, onClose, onCreated }: { ws: string; datasetId?: string; onClose: () => void; onCreated: (projectId: string) => void }) {
  const datasets = useQuery({ queryKey: ["datasets", ws], enabled: !fixed, queryFn: () => api.data.listDatasets({ workspaceId: ws }) });
  const [datasetId, setDatasetId] = useState(fixed ?? "");
  const [name, setName] = useState("");
  const [versionId, setVersionId] = useState("");
  const [review, setReview] = useState("none");
  const [classes, setClasses] = useState<Cls[] | null>(null);
  const schema = useQuery({ queryKey: ["label-schema", ws, datasetId], enabled: !!datasetId, retry: false, queryFn: () => api.labeling.getSchema({ workspaceId: ws, datasetId, version: 0 }) });
  const versions = useQuery({ queryKey: ["versions", ws, datasetId], enabled: !!datasetId, queryFn: () => api.data.listVersions({ workspaceId: ws, datasetId }) });
  const vs = [...(versions.data?.versions ?? [])].sort((a, b) => b.number - a.number);
  const chosen = versionId || vs[0]?.id || "";
  const needClasses = !!datasetId && schema.isError;
  // Prefill the classes from the label files of the version, once per dataset.
  useEffect(() => {
    setClasses(null);
    if (!needClasses || !chosen) return;
    let gone = false;
    void (async () => {
      for (const file of ["data.yaml", "dataset.yaml", "classes.txt", "labels/classes.txt"]) {
        try {
          const r = await api.data.readFile({ workspaceId: ws, datasetId, versionId: chosen, path: file, maxBytes: 65536 });
          const names = parseNames(new TextDecoder().decode(r.data), file);
          if (names.length && !gone) { setClasses(names.map((nm, i) => ({ id: i, name: nm, color: palette[i % palette.length] }))); return; }
        } catch { /* not there */ }
      }
      if (!gone) setClasses([{ id: 0, name: "", color: palette[0] }]);
    })();
    return () => { gone = true; };
  }, [needClasses, chosen, datasetId, ws]);
  const create = useMutation({
    mutationFn: async () => {
      if (needClasses && classes) await api.labeling.createSchemaVersion({ workspaceId: ws, datasetId, task: "", classesJson: JSON.stringify(classes) });
      return api.labeling.createProject({ workspaceId: ws, datasetId, name: name.trim(), reviewMode: review, guidelineMd: "", lockTtlMin: 10, versionId: chosen, autoAcceptThreshold: 0 });
    },
    onSuccess: (p) => onCreated(p.id),
  });
  const ready = !!datasetId && !!name.trim() && !!chosen && (!needClasses || (!!classes && classes.length > 0 && classes.every((c) => c.name.trim())));
  return (
    <Dialog title={t("New labeling project")} onClose={onClose}>
      <div className="space-y-3">
        {!fixed && <Field label="Dataset">
          <select className="input" value={datasetId} onChange={(e) => { setDatasetId(e.target.value); setVersionId(""); }}>
            <option value="">—</option>
            {(datasets.data?.datasets ?? []).map((d) => <option key={d.id} value={d.id}>{d.name} · {d.task}</option>)}
          </select>
        </Field>}
        <Field label={t("Name")}><input className="input" value={name} onChange={(e) => setName(e.target.value)} /></Field>
        {datasetId && <Field label="Version">
          {vs.length === 0 ? <div className="text-sm text-warning">{versions.isLoading ? "…" : t("No version yet")}</div> : (
            <select className="input" value={chosen} onChange={(e) => setVersionId(e.target.value)}>
              {vs.map((v) => <option key={v.id} value={v.id}>v{v.number} · {t("{n} files", { n: String(v.fileCount) })}</option>)}
            </select>)}
        </Field>}
        {needClasses && <Field label={t("Classes")}>
          {!classes ? <div className="skeleton h-8" /> : <div className="space-y-1.5">
            {classes.map((c, i) => (
              <div key={i} className="flex items-center gap-2">
                <input type="color" value={c.color} aria-label={t("Color")} onChange={(e) => setClasses(classes.map((x, j) => j === i ? { ...x, color: e.target.value } : x))} />
                <input className="input" placeholder={t("class name")} value={c.name} onChange={(e) => setClasses(classes.map((x, j) => j === i ? { ...x, name: e.target.value } : x))} />
                <span className="w-8 text-xs text-fg-subtle">#{c.id}</span>
                <button className="text-fg-subtle hover:text-danger" aria-label={t("Delete")} onClick={() => setClasses(classes.filter((_, j) => j !== i))}>✕</button>
              </div>))}
            <button className="btn-ghost h-8" onClick={() => setClasses([...classes, { id: Math.max(-1, ...classes.map((c) => c.id)) + 1, name: "", color: palette[classes.length % palette.length] }])}><Plus className="h-4 w-4" />{t("Class")}</button>
          </div>}
        </Field>}
        <Field label={t("Review")}>
          <select className="input" value={review} onChange={(e) => setReview(e.target.value)}>
            <option value="none">{t("none")}</option><option value="all">{t("every item")}</option>
          </select>
        </Field>
        <ErrorBox error={create.error ? errorText(create.error) : null} />
        <div className="flex justify-end gap-2">
          <button className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-primary" disabled={!ready || create.isPending} onClick={() => create.mutate()}>{t("Create")}</button>
        </div>
      </div>
    </Dialog>
  );
}

/** S-05-1 + S-05-6 + label set: the "Labeling" section of the dataset page. */
export function DatasetLabeling({ ws, datasetId, onOpenProject }: { ws: string; datasetId: string; onOpenProject: (id: string) => void }) {
  const qc = useQueryClient();
  const schema = useQuery({ queryKey: ["label-schema", ws, datasetId], retry: false,
    queryFn: () => api.labeling.getSchema({ workspaceId: ws, datasetId, version: 0 }) });
  const projects = useQuery({ queryKey: ["label-projects", ws, datasetId], queryFn: () => api.labeling.listProjects({ workspaceId: ws, datasetId }) });
  const sets = useQuery({ queryKey: ["label-sets", ws, datasetId], queryFn: () => api.labeling.listLabelSets({ workspaceId: ws, datasetId }) });
  const [editing, setEditing] = useState<Cls[] | null>(null);
  type NewProject = { name: string; reviewMode: string; guideline: string; versionId: string; threshold: number };
  const [newProject, setNewProject] = useState<NewProject | null>(null);
  const refresh = () => ["label-schema", "label-projects", "label-sets"].forEach((k) => qc.invalidateQueries({ queryKey: [k, ws] }));
  const saveSchema = useMutation({
    mutationFn: (cls: Cls[]) => api.labeling.createSchemaVersion({ workspaceId: ws, datasetId, task: "", classesJson: JSON.stringify(cls) }),
    onSuccess: () => { setEditing(null); refresh(); },
  });
  const makeSet = useMutation({ mutationFn: (all: boolean) => api.labeling.createLabelSet({ workspaceId: ws, datasetId, allLabeled: all }), onSuccess: refresh });
  const classes = schema.data ? parseClasses(schema.data.classesJson) : [];

  return (
    <div className="mt-4 grid gap-4 md:grid-cols-3">
      <div className="card">
        <div className="mb-2 flex items-center justify-between text-sm font-medium">
          <span>Label schema {schema.data ? `v${schema.data.version}` : ""}</span>
          <button className="text-brand-500" onClick={() => setEditing(classes.length ? classes : [{ id: 0, name: "", color: palette[0] }])}>{t("Edit")}</button>
        </div>
        {classes.map((c) => (
          <div key={c.id} className="flex items-center gap-2 py-0.5 text-sm"><span className="h-3 w-3 rounded-sm" style={{ background: c.color }} />{c.name}<span className="text-xs text-slate-400">#{c.id}</span></div>))}
        {!schema.data && <div className="text-sm text-slate-500">{t("No schema yet")}</div>}
      </div>
      <div className="card">
        <div className="mb-2 flex items-center justify-between text-sm font-medium">
          <span>Labeling project</span>
          <button className="text-primary" onClick={() => setNewProject({ name: "", reviewMode: "none", guideline: "", versionId: "", threshold: 0 })}>{t("+ Create")}</button>
        </div>
        {(projects.data?.projects ?? []).map((p) => (
          <button key={p.id} className="block w-full border-t border-line py-1.5 text-left text-sm hover:text-primary" onClick={() => onOpenProject(p.id)}>
            <span className="flex items-center justify-between gap-2"><span className="truncate">{p.name}</span><StateBadge state={p.state} /></span>
            <span className="mt-1 block"><ProgressBar p={p} /></span>
            <span className="mt-0.5 block text-xs text-fg-subtle">{doneText(p)}</span>
          </button>))}
        {projects.data?.projects.length === 0 && <div className="text-sm text-slate-500">{t("No projects yet.")}</div>}
      </div>
      <div className="card">
        <div className="mb-2 flex items-center justify-between text-sm font-medium">
          <span>{t("Label set (frozen labels)")}</span>
          <button className="text-brand-500" onClick={() => makeSet.mutate(false)} disabled={makeSet.isPending}>{t("Freeze (accepted)")}</button>
        </div>
        <ErrorBox error={makeSet.error ? errorText(makeSet.error) : null} />
        {(sets.data?.labelSets ?? []).map((s) => (
          <div key={s.id} className="flex justify-between border-t border-slate-100 py-1 text-sm">
            <span>#{s.number} · schema v{s.schemaVersion}</span>
            <span className="text-xs text-slate-500">{t("{items} items · {labels} labels", { items: String(s.itemCount), labels: String(s.annotationCount) })} · {ago(s.createdAt)}</span>
          </div>))}
      </div>

      {editing && (
        <Dialog title={t("Label schema — new version")} onClose={() => setEditing(null)}>
          <div className="space-y-2">
            {editing.map((c, i) => (
              <div key={i} className="flex items-center gap-2">
                <input type="color" value={c.color} onChange={(e) => setEditing(editing.map((x, j) => j === i ? { ...x, color: e.target.value } : x))} />
                <input className="input" placeholder={t("class name")} value={c.name} onChange={(e) => setEditing(editing.map((x, j) => j === i ? { ...x, name: e.target.value } : x))} />
                <span className="w-8 text-xs text-slate-400">#{c.id}</span>
                <button className="text-slate-400" onClick={() => setEditing(editing.filter((_, j) => j !== i))} aria-label={t("Delete")}>✕</button>
              </div>))}
            <button className="btn-ghost" onClick={() => setEditing([...editing, { id: Math.max(-1, ...editing.map((c) => c.id)) + 1, name: "", color: palette[editing.length % palette.length] }])}>+ Class</button>
            <ErrorBox error={saveSchema.error ? errorText(saveSchema.error) : null} />
            <button className="btn-primary" disabled={saveSchema.isPending || editing.some((c) => !c.name.trim())} onClick={() => saveSchema.mutate(editing)}>{t("Save schema")}</button>
          </div>
        </Dialog>)}
      {newProject && <NewLabelingProjectSheet ws={ws} datasetId={datasetId} onClose={() => setNewProject(null)} onCreated={(id) => { setNewProject(null); refresh(); onOpenProject(id); }} />}
    </div>
  );
}

const n = (v: bigint | number | undefined) => Number(v ?? 0);
const pct = (a: number, b: number) => (b > 0 ? Math.round((100 * a) / b) : 0);
const doneText = (p: LabelingProject) => n(p.total) === 0 ? t("No items")
  : t("{done} of {total} accepted ({pct}%)", { done: n(p.accepted), total: n(p.total), pct: pct(n(p.accepted), n(p.total)) });

/** Accepted (green) and waiting-for-review (violet) parts of a project. */
function ProgressBar({ p }: { p: LabelingProject }) {
  const total = n(p.total);
  return (
    <span className="flex h-1.5 overflow-hidden rounded-full bg-surface-2">
      <span style={{ width: `${pct(n(p.accepted), total)}%`, background: barColor.accepted }} />
      <span style={{ width: `${pct(n(p.inReview), total)}%`, background: barColor.in_review }} />
    </span>
  );
}

/** S-05-3 My work: every labeling project in the workspace, the caller's own work first. */
export function LabelingPage({ ws, onOpen, onWork }: { ws: string; onOpen: (id: string) => void; onWork: (id: string, mode: "annotate" | "review") => void; go: (path: string) => void }) {
  const qc = useQueryClient();
  const [creating, setCreating] = useState(false);
  const projects = useQuery({ queryKey: ["label-projects", ws, "all"], queryFn: () => api.labeling.listProjects({ workspaceId: ws, datasetId: "" }), refetchInterval: 15000 });
  const [mine, setMine] = useState(false);
  const activate = useMutation({ mutationFn: (id: string) => api.labeling.setProjectState({ workspaceId: ws, projectId: id, state: "active" }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["label-projects", ws] }) });
  const all = projects.data?.projects ?? [];
  const weight = (p: LabelingProject) => (n(p.myOpen) > 0 ? 0 : p.myRoles.length > 0 ? 1 : 2) + (p.state === "active" ? 0 : 3);
  const list = all.filter((p) => !mine || p.myRoles.length > 0).sort((a, b) => weight(a) - weight(b));
  return (
    <>
      <PageHeader title={t("Labeling")}
        actions={<>
          <label className="flex items-center gap-2 text-sm text-fg-muted"><input type="checkbox" checked={mine} onChange={(e) => setMine(e.target.checked)} />{t("Only my projects")}</label>
          <button className="btn-primary" onClick={() => setCreating(true)}><Plus className="h-4 w-4" />{t("Project")}</button>
        </>} />
      <ErrorBox error={activate.error ? errorText(activate.error) : null} />
      {creating && <NewLabelingProjectSheet ws={ws} onClose={() => setCreating(false)} onCreated={(id) => { setCreating(false); void qc.invalidateQueries({ queryKey: ["label-projects", ws] }); onWork(id, "annotate"); }} />}
      <ErrorBox error={projects.error ? errorText(projects.error) : null} onRetry={() => void projects.refetch()} />
      {list.length === 0 && !projects.isLoading ? (
        <EmptyState title={mine ? t("You are not a member of any labeling project.") : t("No labeling projects yet.")}
          action={<button className="btn-primary" onClick={() => setCreating(true)}><Plus className="h-4 w-4" />{t("Project")}</button>} />) : (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {list.map((p) => {
            const canLabel = p.state === "active" && (p.myRoles.includes("annotator") || p.myRoles.includes("lead"));
            const canReview = p.state === "active" && p.reviewMode === "all" && p.myRoles.includes("reviewer") && n(p.inReview) > 0;
            return (
              <div key={p.id} className="card flex flex-col gap-2 hover:border-primary/40">
                <button className="text-left" onClick={() => onOpen(p.id)}>
                  <span className="flex items-center justify-between gap-2"><span className="truncate font-medium">{p.name}</span><StateBadge state={p.state} /></span>
                  <span className="mt-0.5 flex items-center gap-1 text-xs text-fg-subtle"><Database className="h-3 w-3" />{p.datasetName || "—"} · {ago(p.createdAt)}</span>
                </button>
                <ProgressBar p={p} />
                <div className="text-xs text-fg-muted">{doneText(p)}
                  {n(p.total) > 0 && <> · {t("{n} unlabeled", { n: n(p.unlabeled) })}{p.reviewMode === "all" && <> · {t("{n} in review", { n: n(p.inReview) })}</>}</>}</div>
                <div className="mt-auto flex flex-wrap items-center gap-2 pt-1 text-xs">
                  {p.myRoles.length === 0 ? <span className="text-fg-subtle">{t("You are not a member")}</span>
                    : <span className="text-fg-muted">{p.myRoles.map((r) => t(roleLabel[r] ?? r)).join(", ")}
                        {n(p.myOpen) > 0 && <> · {t("{n} open", { n: n(p.myOpen) })}</>}
                        {n(p.myReturned) > 0 && <span className="text-danger"> · {t("{n} returned to you", { n: n(p.myReturned) })}</span>}</span>}
                  <span className="ml-auto flex gap-2">
                    {p.state !== "active" && p.myRoles.includes("lead") && <button className="btn-primary h-7 px-2 text-xs" disabled={activate.isPending} onClick={() => activate.mutate(p.id)}>{t("Activate")}</button>}
                    {canReview && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => onWork(p.id, "review")}>Review</button>}
                    {canLabel && <button className="btn-primary h-7 px-2 text-xs" onClick={() => onWork(p.id, "annotate")}>{n(p.myOpen) > 0 ? t("Continue") : t("Label")}<ArrowRight className="h-3 w-3" /></button>}
                  </span>
                </div>
              </div>);
          })}
        </div>)}
    </>
  );
}

const roleLabel: Record<string, string> = { lead: "Lead", annotator: "Annotator", reviewer: "Reviewer" };

/** S-05-2 Project dashboard (Lead) + label/review entry points for members. */
export function LabelingProjectPage({ ws, id, onBack, onWork, go }: { ws: string; id: string; onBack: () => void; onWork: (mode: "annotate" | "review") => void; go: (path: string) => void }) {
  const qc = useQueryClient();
  const stats = useQuery({ queryKey: ["label-stats", ws, id], queryFn: () => api.labeling.getProjectStats({ workspaceId: ws, projectId: id }), refetchInterval: 5000 });
  useCrumb(id, stats.data?.project?.name);
  const members = useQuery({ queryKey: ["label-members", ws, id], queryFn: () => api.labeling.listProjectMembers({ workspaceId: ws, projectId: id }), refetchInterval: 15000 });
  const wsMembers = useQuery({ queryKey: ["ws-members", ws], queryFn: () => api.workspace.listMembers({ workspaceId: ws }) });
  const [add, setAdd] = useState({ userId: "", role: "annotator" });
  const [exporting, setExporting] = useState(false);
  const refresh = () => ["label-stats", "label-members", "label-projects", "label-items"].forEach((k) => qc.invalidateQueries({ queryKey: [k, ws] }));
  const setState = useMutation({ mutationFn: (state: string) => api.labeling.setProjectState({ workspaceId: ws, projectId: id, state }), onSuccess: refresh });
  const sync = useMutation({ mutationFn: () => api.labeling.syncProject({ workspaceId: ws, projectId: id }), onSuccess: refresh });
  const [deleting, setDeleting] = useState(false);
  const del = useMutation({ mutationFn: () => api.labeling.deleteProject({ workspaceId: ws, projectId: id }), onSuccess: () => { void qc.invalidateQueries({ queryKey: ["label-projects", ws] }); onBack(); } });
  const addMember = useMutation({ mutationFn: () => api.labeling.addProjectMember({ workspaceId: ws, projectId: id, ...add, maxOpenItems: 50 }), onSuccess: () => { setAdd({ userId: "", role: add.role }); refresh(); } });
  const removeMember = useMutation({ mutationFn: (m: { userId: string; role: string }) => api.labeling.removeProjectMember({ workspaceId: ws, projectId: id, ...m, maxOpenItems: 0 }), onSuccess: refresh });
  const p = stats.data?.project;
  type PTab = "overview" | "items" | "assist" | "hermes" | "quality";
  const [tab, setTabState] = useState<PTab>(() => { const v = new URLSearchParams(window.location.search).get("tab"); return v === "hermes" || v === "assist" || v === "quality" || v === "items" ? v : "overview"; });
  const setTab = (x: PTab) => {
    setTabState(x);
    const u = new URL(window.location.href);
    if (x === "overview") u.searchParams.delete("tab"); else u.searchParams.set("tab", x);
    window.history.replaceState(window.history.state, "", u.toString());
  };
  const total = Number(stats.data?.total ?? 0n);
  const by = stats.data?.byStatus ?? {};
  const lead = p?.myRoles.includes("lead") ?? false;
  const active = p?.state === "active";
  const finished = total > 0 && n(by.accepted) + n(by.skipped) === total;
  const usable = n(by.accepted) + n(by.in_review);
  return (
    <>
      <button className="mb-3 text-sm text-primary" onClick={onBack}>{t("← Labeling")}</button>
      <PageHeader title={p?.name ?? "Project"}
        subtitle={p && <span className="flex flex-wrap items-center gap-2"><StateBadge state={p.state} />
          <button className="inline-flex items-center gap-1 text-fg-muted hover:text-primary" onClick={() => go(`datasets/${p.datasetId}`)}><Database className="h-3.5 w-3.5" />{p.datasetName}</button>
          {p.myRoles.length > 0 && <span className="text-fg-subtle">· {t("You")}: {p.myRoles.map((r) => t(roleLabel[r] ?? r)).join(", ")}</span>}</span>}
        actions={<>
          {active && (p.myRoles.includes("annotator") || lead) && <button className="btn-primary" onClick={() => onWork("annotate")}><Pencil className="h-4 w-4" />{n(p.myOpen) > 0 ? t("Continue labeling ({n})", { n: n(p.myOpen) }) : t("Start labeling")}</button>}
          {active && p.reviewMode === "all" && p.myRoles.includes("reviewer") && <button className="btn-ghost" disabled={n(by.in_review) === 0} onClick={() => onWork("review")}><CheckCheck className="h-4 w-4" />Review ({n(by.in_review)})</button>}
          {lead && (p?.state === "draft" || p?.state === "paused" || p?.state === "completed") && <button className="btn-primary" disabled={setState.isPending} onClick={() => setState.mutate("active")}>{t("Activate")}</button>}
          {lead && active && <button className="btn-ghost" disabled={setState.isPending} onClick={() => setState.mutate("paused")}>{t("Pause")}</button>}
          {lead && active && finished && <button className="btn-ghost" disabled={setState.isPending} onClick={() => setState.mutate("completed")}>{t("Mark completed")}</button>}
          {lead && <button className="btn-ghost" disabled={sync.isPending} onClick={() => sync.mutate()} title={t("Add new files of the dataset")}><FolderSync className="h-4 w-4" />{t("Add new files")}</button>}
          {lead && <button className="btn-ghost px-2 text-danger" aria-label={t("Delete project")} title={t("Delete project")} onClick={() => setDeleting(true)}><Trash2 className="h-4 w-4" /></button>}
        </>} />
      {sync.data && <div className="mb-3 rounded-lg border border-line bg-surface-2 px-3 py-2 text-sm">{t("{n} new items", { n: n(sync.data.added) })}</div>}
      {deleting && p && (
        <ConfirmDialog title={t("Delete project {name}?", { name: p.name })} label="Delete" busy={del.isPending} onClose={() => { setDeleting(false); del.reset(); }}
          onConfirm={() => del.mutate()} consequence={del.error ? <ErrorBox error={errorText(del.error)} /> : null} />)}
      <ErrorBox error={[setState.error, sync.error, stats.error].filter(Boolean).map((e) => errorText(e))[0] ?? null} />
      <Tabs<PTab> tabs={[{ key: "overview", label: t("Overview") }, { key: "items", label: t("Items"), count: total }, { key: "assist", label: t("AI assist") }, { key: "hermes", label: "Hermes" }, { key: "quality", label: t("Quality") }]} value={tab} onChange={setTab} />
      {tab === "quality" ? <QualityProjectTab ws={ws} projectId={id} /> : tab === "hermes" ? <HermesProjectTab ws={ws} projectId={id} /> : tab === "assist" ? <AssistProjectTab ws={ws} projectId={id} />
        : tab === "items" ? <ItemsTab ws={ws} projectId={id} lead={lead} canEdit={!!active && (lead || (p?.myRoles.includes("annotator") ?? false))} onChanged={refresh}
            onEdit={(path) => go(`labeling/${id}/annotate?path=${encodeURIComponent(path)}`)} /> : <>
      <div className="card mb-4">
        <div className="mb-2 flex items-baseline justify-between text-sm">
          <span className="font-medium">{t("Progress")}</span>
          <span className="text-fg-muted">{total === 0 ? "—" : t("{done} of {total} accepted ({pct}%)", { done: n(by.accepted), total, pct: pct(n(by.accepted), total) })}</span>
        </div>
        <div className="flex h-3 overflow-hidden rounded-full bg-surface-2">
          {statusOrder.map((s) => { const c = n(by[s]); return c ? <div key={s} title={`${t(statusLabel[s])} ${c}`} style={{ width: `${(100 * c) / total}%`, background: barColor[s] }} /> : null; })}
        </div>
        <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-fg-muted">
          {statusOrder.map((s) => <button key={s} className="hover:text-fg" onClick={() => { const u = new URL(window.location.href); u.searchParams.set("status", s); window.history.replaceState(window.history.state, "", u.toString()); setTab("items"); }}>
            <span className="mr-1 inline-block h-2 w-2 rounded-full" style={{ background: barColor[s] }} />{t(statusLabel[s])} {n(by[s])}</button>)}
        </div>
      </div>
      <div className="mb-4 grid gap-4 lg:grid-cols-2">
        <div className="card">
          <div className="mb-1 flex items-center gap-2 text-sm font-medium"><PackageCheck className="h-4 w-4 text-fg-subtle" />{t("Use the labels for training")}</div>
          <div className="mt-3 flex items-center gap-3">
            <button className="btn-primary" disabled={usable === 0} onClick={() => setExporting(true)}>{t("Create dataset version")}</button>
            <span className="text-xs text-fg-subtle">{usable === 0 ? t("Nothing to export yet — items must be submitted first") : t("{a} accepted · {r} in review", { a: n(by.accepted), r: n(by.in_review) })}</span>
          </div>
        </div>
        <div className="card">
          <div className="mb-1 text-sm font-medium">{t("Instructions")}</div>
          {p?.guidelineMd ? <div className="max-h-40 overflow-y-auto whitespace-pre-wrap text-sm text-fg-muted">{p.guidelineMd}</div>
            : <div className="text-sm text-fg-subtle">{t("No instructions were written for this project.")}</div>}
          <div className="mt-2 text-xs text-fg-subtle">{t("Review")}: {p?.reviewMode === "all" ? t("every item") : t("none")} · {t("lock {n} min", { n: p?.lockTtlMin ?? 10 })}</div>
        </div>
      </div>
      <div className="card">
        <div className="mb-2 text-sm font-medium">{t("Members")}</div>
        <table className="w-full text-sm">
          <thead><tr className="text-left text-xs text-fg-subtle"><th className="py-1 font-medium">{t("Assignee")}</th><th className="font-medium">{t("Role")}</th>
            <th className="text-right font-medium">{t("Opening")}</th><th className="text-right font-medium">{t("Submitted")}</th><th className="text-right font-medium">{t("Accepted")}</th>
            <th className="text-right font-medium">{t("Returned")}</th><th className="pl-4 font-medium">{t("Last submit")}</th><th /></tr></thead>
          <tbody>
            {(members.data?.members ?? []).map((m) => {
              const ann = m.role === "annotator";
              return (
                <tr key={m.userId + m.role} className="border-t border-line">
                  <td className="py-1.5">{m.username}</td><td className="text-fg-muted">{t(roleLabel[m.role] ?? m.role)}</td>
                  <td className="text-right tabular-nums">{ann ? `${n(m.openItems)}/${m.maxOpenItems}` : "—"}</td>
                  <td className="text-right tabular-nums">{ann ? n(m.submitted) : "—"}</td><td className="text-right tabular-nums">{ann ? n(m.accepted) : "—"}</td>
                  <td className={`text-right tabular-nums ${n(m.returned) > 0 ? "text-danger" : ""}`}>{ann ? n(m.returned) : "—"}</td>
                  <td className="pl-4 text-xs text-fg-muted">{m.lastSubmittedAt ? <RelTime at={m.lastSubmittedAt} /> : "—"}</td>
                  <td className="text-right">{lead && <button className="inline-flex h-7 w-7 items-center justify-center rounded text-fg-subtle hover:bg-surface-2 hover:text-danger" disabled={removeMember.isPending}
                    aria-label={t("Remove {name} as {role}", { name: m.username, role: m.role })} title={t("Remove from the project")} onClick={() => removeMember.mutate({ userId: m.userId, role: m.role })}><Trash2 className="h-4 w-4" /></button>}</td>
                </tr>);
            })}
          </tbody>
        </table>
        {lead && <div className="mt-3 flex gap-2">
          <select className="input" aria-label={t("Workspace member")} value={add.userId} onChange={(e) => setAdd({ ...add, userId: e.target.value })}>
            <option value="">{t("— pick a workspace member —")}</option>
            {(wsMembers.data?.members ?? []).map((m) => <option key={m.userId} value={m.userId}>{m.username} ({m.role})</option>)}
          </select>
          <select className="input w-40" aria-label={t("Role")} value={add.role} onChange={(e) => setAdd({ ...add, role: e.target.value })}>
            <option value="annotator">Annotator</option><option value="reviewer">Reviewer</option><option value="lead">Lead</option>
          </select>
          <button className="btn-ghost" disabled={!add.userId || addMember.isPending} onClick={() => addMember.mutate()}>{t("Add")}</button>
        </div>}
        <ErrorBox error={addMember.error ? errorText(addMember.error) : removeMember.error ? errorText(removeMember.error) : null} />
      </div>
      </>}
      {exporting && p && <ExportSheet ws={ws} project={p} accepted={n(by.accepted)} inReview={n(by.in_review)} onClose={() => setExporting(false)} go={go} />}
    </>
  );
}

/** Labels → dataset version: what goes in, then where to use it. */
function ExportSheet({ ws, project, accepted, inReview, onClose, go }: { ws: string; project: LabelingProject; accepted: number; inReview: number; onClose: () => void; go: (path: string) => void }) {
  const qc = useQueryClient();
  const [all, setAll] = useState(accepted === 0);
  const [note, setNote] = useState("");
  const run = useMutation({
    mutationFn: () => api.labeling.exportLabels({ workspaceId: ws, projectId: project.id, allLabeled: all, note }),
    onSuccess: () => ["versions", "datasets", "dataset"].forEach((k) => qc.invalidateQueries({ queryKey: [k, ws] })),
  });
  const count = all ? accepted + inReview : accepted;
  const r = run.data;
  return (
    <Dialog title={t("Create dataset version from labels")} description={project.datasetName} onClose={onClose}>
      {r ? (
        <div className="space-y-4 text-sm">
          <div className="rounded-lg border border-success/30 bg-success/10 p-3">
            <div className="font-medium">{t("Version v{n} of {dataset} is ready", { n: r.versionNumber, dataset: project.datasetName })}</div>
            <div className="mt-1 text-fg-muted">{t("{items} items · {boxes} boxes · {files} files", { items: n(r.items), boxes: n(r.boxes), files: n(r.fileCount) })}</div>
          </div>
          {n(r.boxes) === 0 && <div className="rounded-lg border border-warning/30 bg-warning/10 p-3 text-warning">{t("These labels are not boxes, so there are no YOLO files; every label is in labels.jsonl.")}</div>}
          {n(r.boxes) > 0 && n(r.otherLabels) > 0 && <div className="text-fg-muted">{t("{n} labels are not boxes — they are only in labels.jsonl.", { n: n(r.otherLabels) })}</div>}
          <TrainWithVersion ws={ws} versionId={r.versionId} datasetId={r.datasetId} go={go} />
        </div>) : (
        <div className="space-y-4 text-sm">
          <Field label={t("Items to include")}>
            <div className="space-y-2">
              <label className="flex items-start gap-2"><input type="radio" className="mt-1" checked={!all} disabled={accepted === 0} onChange={() => setAll(false)} />
                <span>{t("Accepted only ({n})", { n: accepted })}</span></label>
              <label className="flex items-start gap-2"><input type="radio" className="mt-1" checked={all} onChange={() => setAll(true)} />
                <span>{t("Accepted and in review ({n})", { n: accepted + inReview })}</span></label>
            </div>
          </Field>
          <Field label={t("Note")}>
            <input className="input" aria-label={t("Note")} value={note} placeholder={t("Labels of project {name}", { name: project.name })} onChange={(e) => setNote(e.target.value)} /></Field>
          <ErrorBox error={run.error ? errorText(run.error) : null} />
          <button className="btn-primary" disabled={count === 0 || run.isPending} onClick={() => run.mutate()}>{run.isPending ? t("Copying files…") : t("Create version with {n} items", { n: count })}</button>
        </div>)}
    </Dialog>
  );
}

const PAGE = 50;

/** Every item of the project (a Lead) or the caller's own items, by status and file name; a row opens the labels read-only. */
function ItemsTab({ ws, projectId, lead, canEdit, onChanged, onEdit }: { ws: string; projectId: string; lead: boolean; canEdit: boolean; onChanged: () => void; onEdit: (path: string) => void }) {
  const [status, setStatus] = useState(() => new URLSearchParams(window.location.search).get("status") ?? "");
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [mine, setMine] = useState(false);
  const [page, setPage] = useState(0);
  const [open, setOpen] = useState<string | null>(null);
  useEffect(() => { const h = setTimeout(() => { setQuery(search); setPage(0); }, 300); return () => clearTimeout(h); }, [search]);
  const items = useQuery({ queryKey: ["label-items", ws, projectId, status, query, mine, page], placeholderData: (prev) => prev,
    queryFn: () => api.labeling.listItems({ workspaceId: ws, projectId, status, search: query, mine, limit: PAGE, offset: page * PAGE }) });
  const total = n(items.data?.total);
  const rows = items.data?.items ?? [];
  const pick = (s: string) => { setStatus(s); setPage(0); const u = new URL(window.location.href); if (s) u.searchParams.set("status", s); else u.searchParams.delete("status"); window.history.replaceState(window.history.state, "", u.toString()); };
  return (
    <div className="card">
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <input className="input h-8 w-64" placeholder={t("Search file name")} aria-label={t("Search file name")} value={search} onChange={(e) => setSearch(e.target.value)} />
        <select className="input h-8 w-auto" aria-label={t("Status")} value={status} onChange={(e) => pick(e.target.value)}>
          <option value="">{t("Status: all")}</option>{statusOrder.map((s) => <option key={s} value={s}>{t(statusLabel[s])}</option>)}
        </select>
        {lead && <label className="flex items-center gap-2 text-sm text-fg-muted"><input type="checkbox" checked={mine} onChange={(e) => { setMine(e.target.checked); setPage(0); }} />{t("Only mine")}</label>}
        <span className="ml-auto text-xs text-fg-subtle">{t("{n} items", { n: total })}</span>
      </div>
      <ErrorBox error={items.error ? errorText(items.error) : null} onRetry={() => void items.refetch()} />
      <table className="w-full text-sm">
        <thead><tr className="text-left text-xs text-fg-subtle"><th className="py-1 font-medium">{t("File")}</th><th className="font-medium">{t("Status")}</th><th className="text-right font-medium">{t("Labels")}</th>
          <th className="pl-4 font-medium">{t("Annotator")}</th><th className="font-medium">{t("Reviewer")}</th><th className="font-medium">{t("Submitted")}</th>{canEdit && <th />}</tr></thead>
        <tbody>
          {rows.map((i) => (
            <tr key={i.path} className="cursor-pointer border-t border-line hover:bg-surface-2" onClick={() => setOpen(i.path)}>
              <td className="mono max-w-xs truncate py-1.5 pr-3" title={i.path}>{i.path}</td>
              <td><ItemStatus status={i.status} />{i.status === "skipped" && i.skipReason && <span className="ml-2 text-xs text-fg-subtle">{i.skipReason}</span>}</td>
              <td className="text-right tabular-nums">{n(i.labels)}</td>
              <td className="pl-4 text-fg-muted">{i.annotator || "—"}</td><td className="text-fg-muted">{i.reviewer || "—"}</td>
              <td className="text-xs text-fg-muted">{i.submittedAt ? <RelTime at={i.submittedAt} /> : "—"}</td>
              {canEdit && <td className="text-right"><button className="btn-ghost h-7 px-2 text-xs" onClick={(e) => { e.stopPropagation(); onEdit(i.path); }}><Pencil className="h-3.5 w-3.5" />{t("Edit labels")}</button></td>}
            </tr>))}
        </tbody>
      </table>
      {rows.length === 0 && !items.isLoading && <div className="py-8 text-center text-sm text-fg-subtle">{total === 0 && !status && !query ? t("No items") : t("No items match this filter.")}</div>}
      {total > PAGE && <div className="mt-3 flex items-center justify-end gap-2 text-xs text-fg-muted">
        <span>{page * PAGE + 1}–{Math.min(total, (page + 1) * PAGE)} / {total}</span>
        <button className="btn-ghost h-7 px-2 text-xs" disabled={page === 0} onClick={() => setPage(page - 1)}>{t("Previous")}</button>
        <button className="btn-ghost h-7 px-2 text-xs" disabled={(page + 1) * PAGE >= total} onClick={() => setPage(page + 1)}>{t("Next")}</button>
      </div>}
      {open && <ItemSheet ws={ws} projectId={projectId} path={open} lead={lead} onClose={() => setOpen(null)} onChanged={() => { onChanged(); void items.refetch(); }}
        onEdit={canEdit ? () => onEdit(open) : undefined} />}
    </div>
  );
}

function ItemStatus({ status }: { status: string }) {
  return <span className="inline-flex items-center gap-1.5 text-xs"><span className="h-2 w-2 rounded-full" style={{ background: barColor[status] ?? "#94a3b8" }} />{t(statusLabel[status] ?? status)}</span>;
}

const isImage = (path: string) => /\.(jpe?g|png|bmp|webp|gif)$/i.test(path);

/** One item read-only: the picture with its labels, comments, and (Lead) history + reopen. */
function ItemSheet({ ws, projectId, path, lead, onClose, onChanged, onEdit }: { ws: string; projectId: string; path: string; lead: boolean; onClose: () => void; onChanged: () => void; onEdit?: () => void }) {
  const item = useQuery({ queryKey: ["label-item", ws, projectId, path], queryFn: () => api.labeling.getItem({ workspaceId: ws, projectId, path }) });
  const history = useQuery({ queryKey: ["label-history", ws, projectId, path], enabled: lead, retry: false, queryFn: () => api.labeling.getItemHistory({ workspaceId: ws, projectId, path }) });
  const [reason, setReason] = useState("");
  const reopen = useMutation({ mutationFn: () => api.labeling.reopenItem({ workspaceId: ws, projectId, path, text: reason }), onSuccess: () => { onChanged(); void item.refetch(); void history.refetch(); setReason(""); } });
  const it = item.data;
  const classes = it ? parseClasses(it.classesJson) : [];
  const cls = (id: number) => classes.find((c) => c.id === id) ?? { id, name: `#${id}`, color: "#94a3b8" };
  const shapes: Shape[] = (it?.annotations ?? []).flatMap((a): Shape[] => {
    const c = cls(a.classId);
    try {
      const g = JSON.parse(a.geometryJson || "{}") as { x?: number; y?: number; w?: number; h?: number; points?: [number, number][] };
      if (a.kind === "bbox") return [{ classId: a.classId, name: c.name, color: c.color, box: [g.x ?? 0, g.y ?? 0, g.w ?? 0, g.h ?? 0] as Shape["box"], source: "platform" as const }];
      if (a.kind === "polygon" && g.points) return [{ classId: a.classId, name: c.name, color: c.color, points: g.points, source: "platform" as const }];
    } catch { /* not drawable */ }
    return [];
  });
  return (
    <Dialog wide title={path.split("/").pop() ?? path} description={path} onClose={onClose}>
      <ErrorBox error={item.error ? errorText(item.error) : null} />
      {it && <div className="space-y-4 text-sm">
        <div className="flex items-center gap-3"><ItemStatus status={it.status} />{it.skipReason && <span className="text-xs text-fg-muted">{it.skipReason}</span>}
          <span className="ml-auto text-xs text-fg-subtle">{t("{n} labels", { n: it.annotations.length })}</span>
          {onEdit && <button className="btn-primary h-8" onClick={onEdit}><Pencil className="h-4 w-4" />{t("Edit labels")}</button>}</div>
        {isImage(path) && <div className="relative inline-block max-w-full overflow-hidden rounded-lg border border-line"><img src={it.url} alt={path} className="block max-h-[55vh] max-w-full" /><ShapeOverlay shapes={shapes} names /></div>}
        {it.annotations.length > 0 && <div className="flex flex-wrap gap-1.5">
          {it.annotations.map((a, i) => { const c = cls(a.classId); return (
            <span key={i} className="inline-flex items-center gap-1.5 rounded border border-line px-1.5 py-0.5 text-xs"><span className="h-2 w-2 rounded-sm" style={{ background: c.color }} />{c.name}
              <span className="text-fg-subtle">{a.kind}{a.source === "model" ? ` · AI${a.confidence !== undefined ? ` ${Math.round(a.confidence * 100)}%` : ""}` : ""}</span></span>); })}
        </div>}
        {it.comments.length > 0 && <div><div className="mb-1 text-xs font-medium text-fg-muted">{t("Comments")}</div>
          {it.comments.map((c) => <div key={c.id} className="border-t border-line py-1.5"><span className="font-medium">{c.author}</span> <span className="text-xs text-fg-subtle">{ago(c.createdAt)}</span><div className="text-fg-muted">{c.body}</div></div>)}</div>}
        {lead && (history.data?.entries.length ?? 0) > 0 && <div><div className="mb-1 text-xs font-medium text-fg-muted">{t("History")}</div>
          {history.data!.entries.map((e, i) => <div key={i} className="flex justify-between border-t border-line py-1 text-xs"><span>{e.action} · {e.actor}</span><span className="text-fg-subtle">{ago(e.at)}</span></div>)}</div>}
        {lead && it.status === "accepted" && <div className="rounded-lg border border-line p-3">
          <div className="mb-1 font-medium">{t("Reopen this item")}</div>
          <div className="flex gap-2"><input className="input" aria-label={t("Reason")} placeholder={t("Reason")} value={reason} onChange={(e) => setReason(e.target.value)} />
            <button className="btn-ghost shrink-0" disabled={!reason.trim() || reopen.isPending} onClick={() => reopen.mutate()}>{t("Reopen")}</button></div>
          <ErrorBox error={reopen.error ? errorText(reopen.error) : null} />
        </div>}
      </div>}
    </Dialog>
  );
}

const barColor: Record<string, string> = {
  unlabeled: "#94a3b8", in_progress: "#38bdf8", in_review: "#a78bfa", rejected: "#fb7185", skipped: "#fbbf24", accepted: "#34d399",
};

export { Annotator } from "../components/LabelEditor";

/** H1 step 1: "Train with this version" — pick a training project; its canvas opens with the draft set to the new version. */
function TrainWithVersion({ ws, versionId, datasetId, go }: { ws: string; versionId: string; datasetId: string; go: (path: string) => void }) {
  const ds = useQuery({ queryKey: ["dataset", ws, datasetId], queryFn: () => api.data.getDataset({ workspaceId: ws, datasetId }) });
  const projects = useQuery({ queryKey: ["projects", ws], queryFn: () => api.tracking.listProjects({ workspaceId: ws }) });
  const task = ds.data?.task;
  const fits = (projects.data?.projects ?? []).filter((p) => !task || p.task === task);
  const [pick, setPick] = useState("");
  return (
    <div className="space-y-2 rounded-lg border border-line p-3">
      <div className="font-medium">{t("Train with this version")}</div>
      {projects.data && fits.length === 0 && <div className="text-fg-muted">{t("No training project has task {task} yet.", { task: task ?? "" })}</div>}
      {fits.length > 0 && (
        <select className="input" value={pick} onChange={(e) => setPick(e.target.value)} aria-label={t("Training project")}>
          <option value="">{t("Pick a training project…")}</option>
          {fits.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
        </select>)}
      <div className="flex flex-wrap gap-2">
        <button className="btn-primary" disabled={!pick} onClick={() => go(`projects/${pick}?tab=recipe&version=${versionId}`)}>{t("Open the recipe with this version")}</button>
        <button className="btn-ghost" onClick={() => go("projects")}>{t("Create a training project")}</button>
        <button className="btn-ghost" onClick={() => go(`datasets/${datasetId}?tab=versions`)}>{t("Open the dataset")}</button>
      </div>
    </div>
  );
}
