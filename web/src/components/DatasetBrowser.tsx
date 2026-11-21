import { useEffect, useMemo, useRef, useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import {
  ChevronLeft, ChevronRight, ExternalLink, File as FileIcon, FileAudio, FileJson, FileSpreadsheet, FileText, FileVideo, Folder, Home,
  Check, Image as ImageIcon, LayoutGrid, List, Search, Tags, Trash2, Undo2,
} from "lucide-react";
import { api, errorText } from "../lib/api";
import { bytes } from "../lib/format";
import { useSticky } from "../lib/persist";
import { Dialog, EmptyState, ErrorBox, Skeleton } from "./ui";
import { translate as t } from "../i18n";
import type { BrowseFile } from "../gen/mlplatform/v1/data_pb";

/** One drawable label: a box or a polygon in coordinates normalized to 0–1, or an image-level tag. */
export type Shape = { classId: number; name: string; color: string; box?: [number, number, number, number]; points?: [number, number][]; source: "file" | "platform" };
type ClassDef = { id: number; name: string; color?: string };
type VersionOption = { id: string; label: string };

const PALETTE = ["#f43f5e", "#3b82f6", "#22c55e", "#f59e0b", "#a855f7", "#14b8a6", "#ec4899", "#eab308", "#06b6d4", "#84cc16"];
const PAGE = 60;
const KINDS: { key: string; label: string; icon: typeof FileIcon }[] = [
  { key: "image", label: "Images", icon: ImageIcon }, { key: "text", label: "Text", icon: FileText }, { key: "table", label: "Tables", icon: FileSpreadsheet },
  { key: "json", label: "JSON", icon: FileJson }, { key: "audio", label: "Audio", icon: FileAudio }, { key: "video", label: "Video", icon: FileVideo },
  { key: "other", label: "Other", icon: FileIcon },
];
const kindIcon = (k: string) => KINDS.find((x) => x.key === k)?.icon ?? FileIcon;

function classLookup(classesJson: string) {
  let list: ClassDef[] = [];
  try { list = JSON.parse(classesJson || "[]") as ClassDef[]; } catch { /* no schema */ }
  const byId = new Map(list.map((c) => [c.id, c]));
  return (id: number) => {
    const c = byId.get(id);
    return { name: c?.name ?? `#${id}`, color: c?.color || PALETTE[Math.abs(id) % PALETTE.length] };
  };
}

/** YOLO txt: `class cx cy w h` (box) or `class x1 y1 x2 y2 …` (polygon), all normalized. */
function parseYolo(text: string, cls: ReturnType<typeof classLookup>): Shape[] {
  const out: Shape[] = [];
  for (const line of text.split("\n")) {
    const p = line.trim().split(/\s+/);
    if (p.length < 5) continue;
    const id = Number(p[0]);
    const v = p.slice(1).map(Number);
    if (!Number.isInteger(id) || v.some((x) => !Number.isFinite(x))) continue;
    const c = cls(id);
    if (v.length === 4) out.push({ classId: id, ...c, box: [v[0] - v[2] / 2, v[1] - v[3] / 2, v[2], v[3]], source: "file" });
    else if (v.length >= 6 && v.length % 2 === 0) {
      const pts: [number, number][] = [];
      for (let i = 0; i + 1 < v.length; i += 2) pts.push([v[i], v[i + 1]]);
      out.push({ classId: id, ...c, points: pts, source: "file" });
    }
  }
  return out;
}

/** Labels made in the platform (M05): bbox {x,y,w,h}, polygon {points}, tag {} — normalized 0–1. */
function parsePlatform(json: string, cls: ReturnType<typeof classLookup>): Shape[] {
  if (!json) return [];
  try {
    return (JSON.parse(json) as { kind: string; class_id: number; geometry: Record<string, unknown> }[]).map((a) => {
      const c = cls(a.class_id);
      const g = a.geometry ?? {};
      if (a.kind === "bbox") return { classId: a.class_id, ...c, box: [Number(g.x), Number(g.y), Number(g.w), Number(g.h)] as Shape["box"], source: "platform" as const };
      if (a.kind === "polygon" && Array.isArray(g.points)) return { classId: a.class_id, ...c, points: g.points as [number, number][], source: "platform" as const };
      return { classId: a.class_id, ...c, source: "platform" as const };
    });
  } catch { return []; }
}

// Label text cache + a small download queue (labels of a page load a few at a time, not 60 at once).
const labelCache = new Map<string, Promise<string>>();
let active = 0;
const waiting: (() => void)[] = [];
async function slot<T>(job: () => Promise<T>): Promise<T> {
  if (active >= 6) await new Promise<void>((r) => waiting.push(r));
  active++;
  try { return await job(); } finally { active--; waiting.shift()?.(); }
}

type Ctx = { ws: string; datasetId: string; versionId: string };

async function readText(ctx: Ctx, path: string, maxBytes: number): Promise<{ text: string; truncated: boolean; size: number }> {
  const r = await api.data.readFile({ workspaceId: ctx.ws, datasetId: ctx.datasetId, versionId: ctx.versionId, path, maxBytes });
  return { text: new TextDecoder().decode(r.data), truncated: r.truncated, size: Number(r.sizeBytes) };
}

function loadLabel(ctx: Ctx, f: BrowseFile): Promise<string> {
  const key = ctx.versionId + ":" + f.labelPath;
  let p = labelCache.get(key);
  if (!p) {
    // Straight from the source first (fast, cached by the browser); through the api when the source blocks cross-origin reads.
    p = slot(async () => {
      try {
        const res = await fetch(f.labelUrl);
        if (res.ok) return await res.text();
      } catch { /* CORS or network → api */ }
      return (await readText(ctx, f.labelPath, 262144)).text;
    });
    labelCache.set(key, p);
    p.catch(() => labelCache.delete(key));
  }
  return p;
}

/** Shapes of every file on the current page (platform labels immediately, label files as they arrive). */
function usePageShapes(ctx: Ctx, files: BrowseFile[], classesJson: string, enabled: boolean) {
  const cls = useMemo(() => classLookup(classesJson), [classesJson]);
  const [fromFiles, setFromFiles] = useState<Record<string, Shape[]>>({});
  const pageKey = files.map((f) => f.path).join("|");
  useEffect(() => {
    setFromFiles({});
    if (!enabled) return;
    let alive = true;
    for (const f of files) {
      if (!f.labelUrl) continue;
      loadLabel(ctx, f).then((text) => { if (alive) setFromFiles((m) => ({ ...m, [f.path]: parseYolo(text, cls) })); }).catch(() => { /* shown as unlabeled */ });
    }
    return () => { alive = false; };
  }, [pageKey, enabled, cls, ctx.versionId]); // eslint-disable-line react-hooks/exhaustive-deps
  return useMemo(() => {
    const m: Record<string, Shape[]> = {};
    for (const f of files) m[f.path] = [...parsePlatform(f.annotationsJson, cls), ...(fromFiles[f.path] ?? [])];
    return m;
  }, [pageKey, fromFiles, cls]); // eslint-disable-line react-hooks/exhaustive-deps
}

/** Boxes and polygons over an image; the parent must be `relative` and exactly the size of the image. */
export function ShapeOverlay({ shapes, names }: { shapes: Shape[]; names?: boolean }) {
  const drawn = shapes.filter((s) => s.box || s.points);
  if (drawn.length === 0) return null;
  return (
    <>
      <svg className="pointer-events-none absolute inset-0 h-full w-full" viewBox="0 0 1 1" preserveAspectRatio="none">
        {drawn.map((s, i) => s.box
          ? <rect key={i} x={s.box[0]} y={s.box[1]} width={s.box[2]} height={s.box[3]} fill={s.color} fillOpacity={0.08} stroke={s.color} strokeWidth={2} vectorEffect="non-scaling-stroke" />
          : <polygon key={i} points={s.points!.map((p) => p.join(",")).join(" ")} fill={s.color} fillOpacity={0.2} stroke={s.color} strokeWidth={2} vectorEffect="non-scaling-stroke" />)}
      </svg>
      {names && drawn.map((s, i) => {
        const x = s.box ? s.box[0] : Math.min(...s.points!.map((p) => p[0]));
        const y = s.box ? s.box[1] : Math.min(...s.points!.map((p) => p[1]));
        return <span key={i} className="pointer-events-none absolute -translate-y-full whitespace-nowrap rounded-sm px-1 text-[11px] font-semibold leading-4 text-white"
          style={{ left: `${Math.max(0, x) * 100}%`, top: `${Math.max(0.03, y) * 100}%`, background: s.color }}>{s.name}</span>;
      })}
    </>
  );
}

/**
 * S-04-3 Dataset content browser: browse a version (or the working copy) by folder, search by name, filter by file type;
 * images show as a grid with their labels drawn (YOLO files next to the images and labels made in the platform);
 * any file opens in a preview sheet (image + label list, text, JSON, CSV table, audio, video).
 */
/**
 * Content of a dataset. "New files" (added since the latest version) come first while there are any — an upload shows up at once.
 * Files can be selected and removed: a new file is dropped, a file of the latest version is marked and leaves with the next version.
 */
export function DatasetBrowser({ ws, datasetId, versions, workingFiles, removed, onRemove, onKeep, busy }: {
  ws: string; datasetId: string; versions: VersionOption[]; workingFiles: number;
  removed?: Set<string>; onRemove?: (paths: string[], fromNew: boolean) => void; onKeep?: (paths: string[]) => void; busy?: boolean;
}) {
  const options = useMemo(() => [...(workingFiles > 0 || versions.length === 0 ? [{ id: "", label: t("New files ({n})", { n: workingFiles }) }] : []), ...versions], [versions, workingFiles]);
  const [versionId, setVersionId] = useState(options[0]?.id ?? "");
  useEffect(() => { if (!options.some((o) => o.id === versionId)) setVersionId(options[0]?.id ?? ""); }, [options]); // eslint-disable-line react-hooks/exhaustive-deps
  // Right after an upload the new files are what the person wants to see.
  const lastWorking = useRef(workingFiles);
  useEffect(() => { if (workingFiles > lastWorking.current) setVersionId(""); lastWorking.current = workingFiles; }, [workingFiles]);
  const latestId = versions[0]?.id;
  const editable = !!onRemove && (versionId === "" || versionId === latestId);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  useEffect(() => { setPicked(new Set()); }, [versionId]);
  const toggle = (path: string) => setPicked((s) => { const n = new Set(s); if (n.has(path)) n.delete(path); else n.add(path); return n; });
  const [prefix, setPrefix] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [kind, setKind] = useState("");
  const [offset, setOffset] = useState(0);
  const [view, setView] = useSticky<"grid" | "list">("dataset-browser:view", "grid");
  const [showLabels, setShowLabels] = useSticky("dataset-browser:labels", true);
  const [density, setDensity] = useSticky<"s" | "m" | "l">("dataset-browser:density", "m");
  const [open, setOpen] = useState<number | null>(null);
  useEffect(() => { const h = setTimeout(() => setSearch(searchInput), 300); return () => clearTimeout(h); }, [searchInput]);
  useEffect(() => { setOffset(0); }, [versionId, prefix, search, kind]);
  useEffect(() => { setPrefix(""); setSearchInput(""); }, [versionId]);
  const ctx = useMemo(() => ({ ws, datasetId, versionId }), [ws, datasetId, versionId]);

  const stats = useQuery({ queryKey: ["dataset-stats", ws, datasetId, versionId], queryFn: () => api.data.getContentStats({ workspaceId: ws, datasetId, versionId }) });
  const page = useQuery({
    queryKey: ["dataset-browse", ws, datasetId, versionId, prefix, search, kind, offset],
    queryFn: () => api.data.browseFiles({ workspaceId: ws, datasetId, versionId, prefix, search, kind, recursive: false, offset, limit: PAGE }),
    placeholderData: keepPreviousData,
  });
  const files = page.data?.files ?? [];
  const folders = page.data?.folders ?? [];
  const total = Number(page.data?.total ?? 0);
  const shapes = usePageShapes(ctx, files, page.data?.classesJson ?? "[]", showLabels);

  // An image dataset opens on its pictures (all folders); "All" switches to browsing by folder.
  const autoKind = useRef("");
  useEffect(() => {
    if (!stats.data || autoKind.current === versionId) return;
    autoKind.current = versionId;
    if (stats.data.extensions.some((e) => e.kind === "image")) setKind("image");
  }, [stats.data, versionId]);
  const byKind = useMemo(() => {
    const m = new Map<string, number>();
    for (const e of stats.data?.extensions ?? []) m.set(e.kind, (m.get(e.kind) ?? 0) + Number(e.files));
    return m;
  }, [stats.data]);
  const classCounts = useMemo(() => {
    const m = new Map<string, { n: number; color: string }>();
    for (const f of files) for (const s of shapes[f.path] ?? []) m.set(s.name, { n: (m.get(s.name)?.n ?? 0) + 1, color: s.color });
    return [...m.entries()].sort((a, b) => b[1].n - a[1].n);
  }, [files, shapes]);
  const images = files.filter((f) => f.kind === "image");
  const unlabeled = images.filter((f) => !f.labelUrl && !f.annotationsJson).length;
  const gridView = view === "grid" && images.length > 0;
  const crumbs = prefix.split("/").filter(Boolean);
  const cols = { s: "grid-cols-3 sm:grid-cols-4 lg:grid-cols-6 xl:grid-cols-8", m: "grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6", l: "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4" }[density];

  if (options.length === 1 && options[0].id === "" && workingFiles === 0)
    return <EmptyState title={t("This dataset has no content yet")} icon={<Folder className="h-6 w-6" />} />;

  return (
    <div className="space-y-3">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-2">
        <select className="input h-9 w-auto min-w-40" value={versionId} onChange={(e) => setVersionId(e.target.value)} aria-label={t("Version")}>
          {options.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}
        </select>
        {editable && picked.size > 0 && (() => {
          const marked = [...picked].filter((p) => removed?.has(p));
          return (<>
            {marked.length < picked.size && <button className="btn-ghost h-9 text-danger" disabled={busy}
              onClick={() => { onRemove!([...picked].filter((p) => !removed?.has(p)), versionId === ""); setPicked(new Set()); }}><Trash2 className="h-4 w-4" />{t("Remove {n}", { n: picked.size - marked.length })}</button>}
            {marked.length > 0 && onKeep && <button className="btn-ghost h-9" disabled={busy} onClick={() => { onKeep(marked); setPicked(new Set()); }}><Undo2 className="h-4 w-4" />{t("Keep {n}", { n: marked.length })}</button>}
            <button className="btn-ghost h-9" onClick={() => setPicked(new Set())}>{t("Clear selection")}</button>
          </>);
        })()}
        <div className="relative min-w-48 flex-1">
          <Search className="pointer-events-none absolute left-2.5 top-2.5 h-4 w-4 text-fg-subtle" />
          <input className="input h-9 pl-8" value={searchInput} onChange={(e) => setSearchInput(e.target.value)} placeholder={t("Search file names in this folder and below…")} />
        </div>
        <label className={`inline-flex h-9 cursor-pointer items-center gap-1.5 rounded-md border px-2.5 text-sm ${showLabels ? "border-primary/50 bg-primary/10 text-fg" : "border-line text-fg-muted"}`}>
          <input type="checkbox" className="sr-only" checked={showLabels} onChange={(e) => setShowLabels(e.target.checked)} /><Tags className="h-4 w-4" />{t("Labels")}
        </label>
        <div className="inline-flex h-9 overflow-hidden rounded-md border border-line text-sm">
          <button className={`px-2.5 ${view === "grid" ? "bg-surface-2 text-fg" : "text-fg-muted"}`} onClick={() => setView("grid")} aria-label={t("Grid")}><LayoutGrid className="h-4 w-4" /></button>
          <button className={`px-2.5 ${view === "list" ? "bg-surface-2 text-fg" : "text-fg-muted"}`} onClick={() => setView("list")} aria-label={t("List")}><List className="h-4 w-4" /></button>
        </div>
        {gridView && (
          <div className="inline-flex h-9 overflow-hidden rounded-md border border-line text-xs">
            {(["s", "m", "l"] as const).map((d) => <button key={d} className={`px-2.5 uppercase ${density === d ? "bg-surface-2 text-fg" : "text-fg-muted"}`} onClick={() => setDensity(d)} aria-label={t("Thumbnail size {s}", { s: d.toUpperCase() })}>{d}</button>)}
          </div>)}
      </div>

      {/* File types */}
      <div className="flex flex-wrap items-center gap-1.5 text-xs">
        <button className={`rounded-full border px-2.5 py-1 ${kind === "" ? "border-primary/50 bg-primary/10 text-fg" : "border-line text-fg-muted hover:text-fg"}`} onClick={() => setKind("")}>
          {t("All")} <b className="mono">{String(stats.data?.files ?? 0)}</b></button>
        {KINDS.filter((k) => byKind.has(k.key) && k.key !== "other").map((k) => (
          <button key={k.key} className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-1 ${kind === k.key ? "border-primary/50 bg-primary/10 text-fg" : "border-line text-fg-muted hover:text-fg"}`}
            onClick={() => setKind(kind === k.key ? "" : k.key)}><k.icon className="h-3.5 w-3.5" />{t(k.label)} <b className="mono">{byKind.get(k.key)}</b></button>))}
        {byKind.has("other") && <span className="px-1 text-fg-subtle">{t("{n} other files", { n: byKind.get("other") ?? 0 })}</span>}
        <span className="ml-auto text-fg-subtle">{bytes(stats.data?.sizeBytes ?? 0)}</span>
      </div>

      {/* Path */}
      <div className="flex flex-wrap items-center gap-1 text-sm">
        <button className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-fg-muted hover:bg-surface-2 hover:text-fg" onClick={() => setPrefix("")}><Home className="h-3.5 w-3.5" />{t("Root")}</button>
        {crumbs.map((c, i) => (
          <span key={i} className="inline-flex items-center gap-1"><ChevronRight className="h-3.5 w-3.5 text-fg-subtle" />
            <button className={`rounded px-1.5 py-0.5 hover:bg-surface-2 ${i === crumbs.length - 1 ? "font-medium text-fg" : "text-fg-muted"}`} onClick={() => setPrefix(crumbs.slice(0, i + 1).join("/"))}>{c}</button></span>))}
      </div>

      <ErrorBox error={page.error ? errorText(page.error) : null} onRetry={() => void page.refetch()} />
      {page.isLoading && <Skeleton rows={6} />}

      {/* Folders */}
      {folders.length > 0 && (
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
          {folders.map((f) => (
            <button key={f.name} className="flex items-center gap-2.5 rounded-lg border border-line bg-surface px-3 py-2 text-left hover:border-primary/50"
              onClick={() => setPrefix((prefix ? prefix + "/" : "") + f.name)}>
              <Folder className="h-5 w-5 shrink-0 text-amber-400" />
              <span className="min-w-0 flex-1"><span className="block truncate text-sm font-medium">{f.name}</span>
                <span className="text-xs text-fg-subtle">{t("{n} files", { n: String(f.files) })} · {bytes(f.sizeBytes)}</span></span>
            </button>))}
        </div>)}

      {/* Label summary of the page */}
      {showLabels && images.length > 0 && (
        <div className="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
          <span>{t("Labels on this page")}:</span>
          {classCounts.length === 0 && <span className="text-fg-subtle">{t("none")}</span>}
          {classCounts.map(([name, v]) => <span key={name} className="inline-flex items-center gap-1 rounded-full border border-line px-2 py-0.5"><span className="h-2 w-2 rounded-full" style={{ background: v.color }} />{name} <b className="mono text-fg">{v.n}</b></span>)}
          {unlabeled > 0 && <span className="text-amber-500">{t("{n} images without labels", { n: unlabeled })}</span>}
        </div>)}

      {/* Files */}
      {gridView ? (
        <div className={`grid items-start gap-2.5 ${cols}`}>
          {files.map((f, i) => f.kind === "image" ? (
            <button key={f.path} className={`group overflow-hidden rounded-lg border bg-surface text-left hover:border-primary/60 ${picked.has(f.path) ? "border-primary ring-2 ring-primary/40" : "border-line"} ${removed?.has(f.path) && versionId !== "" ? "opacity-40" : ""}`} onClick={() => setOpen(i)} title={f.path}>
              <div className="relative bg-surface-2">
                <img src={f.url} alt={f.path} loading="lazy" decoding="async" className="block h-auto w-full" />
                {showLabels && <ShapeOverlay shapes={shapes[f.path] ?? []} />}
                {editable && (
                  <span role="checkbox" aria-checked={picked.has(f.path)} aria-label={t("Select")} tabIndex={0}
                    className={`absolute left-1.5 top-1.5 flex h-5 w-5 items-center justify-center rounded border bg-surface/90 ${picked.has(f.path) ? "border-primary bg-primary text-white" : "border-line touch-show opacity-0 group-hover:opacity-100"}`}
                    onClick={(e) => { e.stopPropagation(); toggle(f.path); }} onKeyDown={(e) => { if (e.key === " ") { e.preventDefault(); e.stopPropagation(); toggle(f.path); } }}>
                    {picked.has(f.path) && <Check className="h-3.5 w-3.5" />}</span>)}
                {removed?.has(f.path) && versionId !== "" && <span className="absolute right-1.5 top-1.5 rounded bg-danger px-1.5 py-0.5 text-[10px] font-medium text-white">{t("removed in next version")}</span>}
              </div>
              <div className="flex items-center justify-between gap-2 px-2 py-1 text-[11px] text-fg-muted">
                <span className="truncate">{f.path.split("/").pop()}</span>
                {showLabels && (shapes[f.path]?.length ?? 0) > 0 && <span className="mono shrink-0 rounded bg-surface-2 px-1">{shapes[f.path].length}</span>}
              </div>
            </button>
          ) : <FileTile key={f.path} f={f} onOpen={() => setOpen(i)} />)}
        </div>
      ) : files.length > 0 && (
        <div className="overflow-hidden rounded-lg border border-line">
          <table className="w-full text-sm">
            <thead className="bg-surface-2 text-left text-xs text-fg-muted"><tr>{editable && <th className="w-8 pl-3" />}<th className="px-3 py-2">{t("Name")}</th><th className="w-28">{t("Type")}</th><th className="w-24">{t("Labels")}</th><th className="w-24 pr-3 text-right">{t("Size")}</th></tr></thead>
            <tbody>
              {files.map((f, i) => {
                const Icon = kindIcon(f.kind);
                return (
                  <tr key={f.path} className={`cursor-pointer border-t border-line hover:bg-surface-2 ${removed?.has(f.path) && versionId !== "" ? "text-fg-subtle line-through" : ""}`} onClick={() => setOpen(i)}>
                    {editable && <td className="pl-3" onClick={(e) => e.stopPropagation()}><input type="checkbox" aria-label={t("Select")} checked={picked.has(f.path)} onChange={() => toggle(f.path)} /></td>}
                    <td className="px-3 py-1.5"><span className="inline-flex items-center gap-2"><Icon className="h-4 w-4 shrink-0 text-fg-subtle" /><span className="mono text-xs">{search || kind ? f.path : f.path.split("/").pop()}</span></span></td>
                    <td className="text-xs text-fg-muted">{t(KINDS.find((k) => k.key === f.kind)?.label ?? "Other")}</td>
                    <td className="mono text-xs text-fg-muted">{f.kind === "image" ? (shapes[f.path]?.length ?? (f.labelUrl ? "…" : "—")) : ""}</td>
                    <td className="mono pr-3 text-right text-xs text-fg-muted">{bytes(f.sizeBytes)}</td>
                  </tr>);
              })}
            </tbody>
          </table>
        </div>)}
      {!page.isLoading && files.length === 0 && folders.length === 0 && <EmptyState bare title={search || kind ? t("No file matches") : t("This folder is empty")} />}

      {/* Paging */}
      {total > PAGE && (
        <div className="flex items-center justify-end gap-3 text-sm text-fg-muted">
          <span className="mono text-xs">{offset + 1}–{Math.min(offset + PAGE, total)} / {total}</span>
          <button className="btn-ghost h-8 px-2" disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - PAGE))}><ChevronLeft className="h-4 w-4" />{t("Previous")}</button>
          <button className="btn-ghost h-8 px-2" disabled={offset + PAGE >= total} onClick={() => setOffset(offset + PAGE)}>{t("Next")}<ChevronRight className="h-4 w-4" /></button>
        </div>)}

      {open !== null && files[open] && (
        <FilePreview ctx={ctx} file={files[open]} shapes={shapes[files[open].path] ?? []} showLabels={showLabels} setShowLabels={setShowLabels}
          position={`${offset + open + 1} / ${total}`} onClose={() => setOpen(null)}
          onPrev={open > 0 ? () => setOpen(open - 1) : undefined} onNext={open < files.length - 1 ? () => setOpen(open + 1) : undefined} />)}
    </div>
  );
}

function FileTile({ f, onOpen }: { f: BrowseFile; onOpen: () => void }) {
  const Icon = kindIcon(f.kind);
  return (
    <button className="flex aspect-[4/3] flex-col items-center justify-center gap-2 rounded-lg border border-line bg-surface p-2 text-center hover:border-primary/60" onClick={onOpen} title={f.path}>
      <Icon className="h-7 w-7 text-fg-subtle" />
      <span className="w-full truncate text-[11px] text-fg-muted">{f.path.split("/").pop()}</span>
      <span className="mono text-[10px] text-fg-subtle">{bytes(f.sizeBytes)}</span>
    </button>
  );
}

function FilePreview({ ctx, file, shapes, showLabels, setShowLabels, position, onClose, onPrev, onNext }: {
  ctx: Ctx; file: BrowseFile; shapes: Shape[]; showLabels: boolean; setShowLabels: (v: boolean) => void; position: string;
  onClose: () => void; onPrev?: () => void; onNext?: () => void;
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "ArrowLeft") onPrev?.(); if (e.key === "ArrowRight") onNext?.(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });
  return (
    <Dialog title={file.path.split("/").pop() ?? file.path} onClose={onClose} xwide
      description={<span className="mono break-all text-xs">{file.path} · {bytes(file.sizeBytes)}</span>}>
      <div className="space-y-3">
        <div className="flex items-center gap-2 text-sm">
          <button className="btn-ghost h-8 px-2" disabled={!onPrev} onClick={onPrev}><ChevronLeft className="h-4 w-4" />{t("Previous")}</button>
          <button className="btn-ghost h-8 px-2" disabled={!onNext} onClick={onNext}>{t("Next")}<ChevronRight className="h-4 w-4" /></button>
          <span className="mono text-xs text-fg-subtle">{position}</span>
          <span className="text-xs text-fg-subtle">{t("← → to move")}</span>
          <a className="btn-ghost ml-auto h-8 px-2" href={file.url} target="_blank" rel="noreferrer"><ExternalLink className="h-4 w-4" />{t("Open original")}</a>
        </div>
        {file.kind === "image" && <ImagePreview ctx={ctx} file={file} shapes={shapes} showLabels={showLabels} setShowLabels={setShowLabels} />}
        {file.kind === "audio" && <audio controls src={file.url} className="w-full" />}
        {file.kind === "video" && <video controls src={file.url} className="max-h-[70vh] w-full rounded-lg bg-black" />}
        {(file.kind === "text" || file.kind === "json") && <TextPreview ctx={ctx} file={file} />}
        {file.kind === "table" && <TablePreview ctx={ctx} file={file} />}
        {file.kind === "other" && <div className="rounded-lg border border-line p-4 text-sm text-fg-muted">{t("No preview for this file type. Use \"Open original\" to download it.")}</div>}
      </div>
    </Dialog>
  );
}

function ImagePreview({ ctx, file, shapes, showLabels, setShowLabels }: { ctx: Ctx; file: BrowseFile; shapes: Shape[]; showLabels: boolean; setShowLabels: (v: boolean) => void }) {
  const [size, setSize] = useState<{ w: number; h: number } | null>(null);
  const [focus, setFocus] = useState<number | null>(null);
  const raw = useQuery({ queryKey: ["label-raw", ctx.versionId, file.labelPath], enabled: !!file.labelUrl, queryFn: () => loadLabel(ctx, file) });
  useEffect(() => { setSize(null); setFocus(null); }, [file.path]);
  const shown = focus === null ? shapes : shapes.filter((_, i) => i === focus);
  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]">
      <div className="flex items-start justify-center rounded-lg bg-surface-2 p-2">
        <div className="relative inline-block">
          <img src={file.url} alt={file.path} className="block max-h-[72vh] max-w-full" onLoad={(e) => setSize({ w: e.currentTarget.naturalWidth, h: e.currentTarget.naturalHeight })} />
          {showLabels && <ShapeOverlay shapes={shown} names />}
        </div>
      </div>
      <div className="space-y-3 text-sm">
        <div className="flex items-center justify-between">
          <span className="font-semibold">{t("Labels")} <span className="mono font-normal text-fg-muted">{shapes.length}</span></span>
          <label className="inline-flex items-center gap-1.5 text-xs text-fg-muted"><input type="checkbox" checked={showLabels} onChange={(e) => setShowLabels(e.target.checked)} />{t("Show on image")}</label>
        </div>
        {size && <div className="mono text-xs text-fg-subtle">{size.w} × {size.h} px</div>}
        {shapes.length === 0 && <div className="rounded-lg border border-dashed border-line p-3 text-xs text-fg-muted">{file.labelUrl ? t("The label file is empty (no objects).") : t("No labels for this image.")}</div>}
        <div className="max-h-[46vh] space-y-1 overflow-y-auto">
          {shapes.map((s, i) => (
            <button key={i} className={`flex w-full items-center gap-2 rounded-md border px-2 py-1 text-left text-xs ${focus === i ? "border-primary bg-primary/10" : "border-line hover:bg-surface-2"}`}
              onMouseEnter={() => setFocus(i)} onMouseLeave={() => setFocus(null)}>
              <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ background: s.color }} />
              <span className="min-w-0 flex-1 truncate font-medium">{s.name}</span>
              <span className="mono text-fg-subtle">{s.box && size ? `${Math.round(s.box[2] * size.w)}×${Math.round(s.box[3] * size.h)}` : s.points ? t("polygon") : s.box ? "" : t("tag")}</span>
              <span className="text-[10px] text-fg-subtle">{s.source === "platform" ? t("platform") : t("file")}</span>
            </button>))}
        </div>
        {file.labelUrl && (
          <details className="text-xs">
            <summary className="cursor-pointer text-fg-muted">{t("Label file")} <span className="mono">{file.labelPath.split("/").pop()}</span></summary>
            <pre className="mono mt-1 max-h-40 overflow-auto rounded bg-surface-2 p-2 text-[11px]">{raw.data ?? "…"}</pre>
          </details>)}
      </div>
    </div>
  );
}

function TextPreview({ ctx, file }: { ctx: Ctx; file: BrowseFile }) {
  const q = useQuery({ queryKey: ["file-text", ctx.versionId, file.path], queryFn: () => readText(ctx, file.path, 262144) });
  const text = useMemo(() => {
    if (!q.data) return "";
    if (file.path.toLowerCase().endsWith(".json") && !q.data.truncated) { try { return JSON.stringify(JSON.parse(q.data.text), null, 2); } catch { /* show as is */ } }
    return q.data.text;
  }, [q.data, file.path]);
  if (q.isLoading) return <Skeleton rows={6} />;
  if (q.error) return <ErrorBox error={errorText(q.error)} onRetry={() => void q.refetch()} />;
  return (
    <div className="space-y-1">
      <div className="text-xs text-fg-subtle">{t("{n} lines shown", { n: text.split("\n").length })}{q.data?.truncated && <> · <span className="text-amber-500">{t("first {a} of {b}", { a: bytes(262144), b: bytes(q.data.size) })}</span></>}</div>
      <pre className="mono max-h-[72vh] overflow-auto whitespace-pre-wrap break-words rounded-lg bg-surface-2 p-3 text-xs leading-5">{text}</pre>
    </div>
  );
}

/** Minimal CSV/TSV reader: quoted fields, escaped quotes, newlines inside quotes. */
function parseDelimited(text: string, sep: string, maxRows: number): string[][] {
  const rows: string[][] = [];
  let row: string[] = [], cell = "", quoted = false;
  for (let i = 0; i < text.length && rows.length < maxRows; i++) {
    const ch = text[i];
    if (quoted) {
      if (ch === '"') { if (text[i + 1] === '"') { cell += '"'; i++; } else quoted = false; } else cell += ch;
    } else if (ch === '"') quoted = true;
    else if (ch === sep) { row.push(cell); cell = ""; }
    else if (ch === "\n") { row.push(cell); rows.push(row); row = []; cell = ""; }
    else if (ch !== "\r") cell += ch;
  }
  if ((cell || row.length) && rows.length < maxRows) { row.push(cell); rows.push(row); }
  return rows;
}

function TablePreview({ ctx, file }: { ctx: Ctx; file: BrowseFile }) {
  const q = useQuery({ queryKey: ["file-table", ctx.versionId, file.path], queryFn: () => readText(ctx, file.path, 1048576) });
  const rows = useMemo(() => {
    if (!q.data) return [];
    const all = parseDelimited(q.data.text, file.path.toLowerCase().endsWith(".tsv") ? "\t" : ",", 501);
    return q.data.truncated ? all.slice(0, -1) : all;   // the last row of a cut file may be incomplete
  }, [q.data, file.path]);
  const scroller = useRef<HTMLDivElement>(null);
  if (q.isLoading) return <Skeleton rows={6} />;
  if (q.error) return <ErrorBox error={errorText(q.error)} onRetry={() => void q.refetch()} />;
  const [head, ...body] = rows;
  return (
    <div className="space-y-1">
      <div className="text-xs text-fg-subtle">{t("{c} columns · first {r} rows", { c: head?.length ?? 0, r: body.length })}{q.data?.truncated && <> · <span className="text-amber-500">{t("first {a} of {b}", { a: bytes(1048576), b: bytes(q.data.size) })}</span></>}</div>
      <div ref={scroller} className="max-h-[72vh] overflow-auto rounded-lg border border-line">
        <table className="w-max min-w-full text-xs">
          <thead className="sticky top-0 bg-surface-2 text-left"><tr><th className="px-2 py-1.5 text-fg-subtle">#</th>{head?.map((h, i) => <th key={i} className="whitespace-nowrap px-2 py-1.5 font-semibold">{h}</th>)}</tr></thead>
          <tbody>
            {body.map((r, i) => (
              <tr key={i} className="border-t border-line"><td className="mono px-2 py-1 text-fg-subtle">{i + 1}</td>
                {r.map((c, j) => <td key={j} className="mono max-w-80 truncate px-2 py-1" title={c}>{c}</td>)}</tr>))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/** Up to four sample images of the newest version, for dataset cards. */
export function DatasetThumbs({ ws, datasetId }: { ws: string; datasetId: string }) {
  const q = useQuery({ queryKey: ["dataset-thumbs", ws, datasetId], staleTime: 300000,
    queryFn: () => api.data.browseFiles({ workspaceId: ws, datasetId, versionId: "latest", prefix: "", search: "", kind: "image", recursive: true, offset: 0, limit: 4 }) });
  const imgs = q.data?.files ?? [];
  if (imgs.length === 0) return null;
  return (
    <div className="mt-3 grid grid-cols-4 gap-1">
      {imgs.map((f) => <img key={f.path} src={f.url} alt="" loading="lazy" className="aspect-square w-full rounded object-cover" />)}
    </div>
  );
}
