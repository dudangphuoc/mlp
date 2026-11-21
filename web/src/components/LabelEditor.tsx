import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import {
  ArrowLeft, BookOpen, CheckCheck, Power, ChevronLeft, Loader2, Wand2, ChevronRight, PanelBottom, PanelLeft, Pencil, Brush, Circle, Eraser, Eye, EyeOff, Hand, Maximize, MousePointer2, Pentagon, Redo2, Save, Send, SkipForward, Square, Tag, Trash2, Undo2,
} from "lucide-react";
import { api, errorText } from "../lib/api";
import { ago } from "../lib/format";
import type { HermesCheck, LabelItem } from "../gen/mlplatform/v1/labeling_pb";
import { HermesPanel, LastSubmitCard } from "./Hermes";
import { AssistPanel, toAnns, type ProposedAnn } from "./Assist";
import { useQuery } from "@tanstack/react-query";
import { useNavigate, useRouterState } from "@tanstack/react-router";
import { Dialog, ErrorBox, StateBadge, Tip } from "./ui";
import { translate as t } from "../i18n";
import { useSticky } from "../lib/persist";

/*
 * Labeling editor (M05 S-05-4/5). One editor for images, text, audio and video; the media type comes from the file extension.
 * Annotations are stored generically: kind + class + geometry (normalized 0..1 for images/video, character offsets for text,
 * seconds for audio/video segments). Locking, drafts (auto-save every 5 s), submit/skip and review are shared by all media.
 */

export type Cls = { id: number; name: string; color: string };
export type Ann = { kind: string; classId: number; geometry: Record<string, unknown>; source: string; confidence?: number; hidden?: boolean };
type Media = "image" | "text" | "audio" | "video";

const EXT: Record<Media, string[]> = {
  image: ["jpg", "jpeg", "png", "webp", "bmp", "gif", "tif", "tiff"],
  text: ["txt", "md", "json", "jsonl", "html", "xml", "csv", "tsv"],
  audio: ["wav", "mp3", "flac", "ogg", "m4a", "aac", "opus"],
  video: ["mp4", "webm", "mov", "mkv", "avi"],
};
export const mediaOf = (path: string): Media => {
  const e = path.split(".").pop()?.toLowerCase() ?? "";
  return (Object.keys(EXT) as Media[]).find((m) => EXT[m].includes(e)) ?? "image";
};
const r4 = (v: number) => Math.round(Math.min(1, Math.max(0, v)) * 10000) / 10000;
/** High-contrast palette (distinct hues, readable on dark and light images). */
const PALETTE = ["#ef4444", "#3b82f6", "#22c55e", "#f59e0b", "#a855f7", "#06b6d4", "#ec4899", "#84cc16", "#f97316", "#14b8a6", "#6366f1", "#eab308"];
/** Classes of the schema; a class without a color, or with one another class already uses, gets its own palette color. */
const parseClasses = (json: string): Cls[] => {
  let list: Cls[];
  try { list = JSON.parse(json) as Cls[]; } catch { return []; }
  const used = new Set<string>();
  return list.map((c, i) => {
    let color = typeof c.color === "string" && /^#[0-9a-f]{6}$/i.test(c.color) ? c.color.toLowerCase() : "";
    if (!color || used.has(color)) color = PALETTE.find((p) => !used.has(p)) ?? PALETTE[i % PALETTE.length];
    used.add(color);
    return { ...c, color };
  });
};

const fromItem = (item: LabelItem): Ann[] => item.annotations.map((a) => ({
  kind: a.kind, classId: a.classId, source: a.source, confidence: a.confidence,
  geometry: (() => { try { return JSON.parse(a.geometryJson) as Record<string, unknown>; } catch { return {}; } })(),
}));
const fromDraft = (json: string): Ann[] => {
  try {
    return (JSON.parse(json) as { kind: string; class_id: number; geometry: Record<string, unknown>; source: string }[])
      .map((a) => ({ kind: a.kind, classId: a.class_id, geometry: a.geometry, source: a.source }));
  } catch { return []; }
};

/** Undo/redo history over the annotation list. */
function useHistory(initial: Ann[]) {
  const [state, setState] = useState({ past: [] as Ann[][], now: initial, future: [] as Ann[][] });
  const set = useCallback((next: Ann[] | ((a: Ann[]) => Ann[])) => setState((s) => {
    const n = typeof next === "function" ? next(s.now) : next;
    return { past: [...s.past.slice(-99), s.now], now: n, future: [] };
  }), []);
  const reset = useCallback((n: Ann[]) => setState({ past: [], now: n, future: [] }), []);
  const undo = useCallback(() => setState((s) => (s.past.length ? { past: s.past.slice(0, -1), now: s.past[s.past.length - 1], future: [s.now, ...s.future] } : s)), []);
  const redo = useCallback(() => setState((s) => (s.future.length ? { past: [...s.past, s.now], now: s.future[0], future: s.future.slice(1) } : s)), []);
  return { anns: state.now, set, reset, undo, redo, canUndo: state.past.length > 0, canRedo: state.future.length > 0 };
}

/** Run-length encoding of a binary mask (row-major, counts start with a run of 0s). */
function encodeRle(bits: Uint8Array, w: number, h: number) {
  const counts: number[] = [];
  let cur = 0, run = 0;
  for (let i = 0; i < bits.length; i++) {
    if (bits[i] === cur) run++;
    else { counts.push(run); cur = bits[i]; run = 1; }
  }
  counts.push(run);
  return { size: [h, w], counts };
}
function decodeRle(rle: { size: number[]; counts: number[] }) {
  const [h, w] = rle.size;
  const bits = new Uint8Array(w * h);
  let i = 0, v = 0;
  for (const c of rle.counts) { if (v) bits.fill(1, i, i + c); i += c; v ^= 1; }
  return { bits, w, h };
}

/* ═════════════════════════════ Shell: lock, draft, submit, review ═════════════════════════════ */

const EDITABLE = ["unlabeled", "returned", "skipped", "in_progress"];
const STATUS_DOT: Record<string, string> = {
  unlabeled: "bg-fg-subtle/40", in_progress: "bg-primary", in_review: "bg-warning", returned: "bg-danger", skipped: "bg-fg-subtle", accepted: "bg-success",
};
const STRIP = 200;

/** Axis-aligned box of a label (boxes as they are, polygons by their points), normalized 0..1; null for tags / text. */
function boxOf(a: Ann): [number, number, number, number] | null {
  const g = a.geometry as Record<string, number> & { points?: number[][] };
  if (a.kind === "bbox") return [g.x, g.y, g.x + g.w, g.y + g.h];
  if (g.points?.length) { const xs = g.points.map((p) => p[0]), ys = g.points.map((p) => p[1]); return [Math.min(...xs), Math.min(...ys), Math.max(...xs), Math.max(...ys)]; }
  return null;
}
function iouOf(a: [number, number, number, number], b: [number, number, number, number]) {
  const ix = Math.max(0, Math.min(a[2], b[2]) - Math.max(a[0], b[0])), iy = Math.max(0, Math.min(a[3], b[3]) - Math.max(a[1], b[1]));
  const inter = ix * iy, u = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter;
  return u > 0 ? inter / u : 0;
}
/**
 * AI ↔ person agreement on one image: each AI box against its best-overlapping person box (and back). Matched = IoU ≥ 0.5 and same class.
 */
function agreement(anns: Ann[]) {
  const ai = anns.map((a, i) => [a, i] as const).filter(([a]) => a.source === "model" && boxOf(a));
  const hu = anns.map((a, i) => [a, i] as const).filter(([a]) => a.source !== "model" && boxOf(a));
  const match = new Map<number, { iou: number; same: boolean }>();
  const best = (from: typeof ai, to: typeof ai) => from.map(([a, i]) => {
    let top = { iou: 0, same: false };
    for (const [b] of to) { const v = iouOf(boxOf(a)!, boxOf(b)!); if (v > top.iou) top = { iou: v, same: a.classId === b.classId }; }
    match.set(i, top);
    return top;
  });
  const aiBest = best(ai, hu), huBest = best(hu, ai);
  const ok = aiBest.filter((m) => m.iou >= 0.5 && m.same);
  return {
    match, ai: ai.length, human: hu.length, matched: ok.length,
    meanIou: ok.length ? ok.reduce((n, m) => n + m.iou, 0) / ok.length : 0,
    wrongClass: aiBest.filter((m) => m.iou >= 0.5 && !m.same).length,
    aiOnly: aiBest.filter((m) => m.iou < 0.5).length, humanOnly: huBest.filter((m) => m.iou < 0.5).length,
  };
}

/** Window width, kept current on resize. */
function useWindowWidth() {
  const [w, setW] = useState(() => window.innerWidth);
  useEffect(() => { const on = () => setW(window.innerWidth); window.addEventListener("resize", on); return () => window.removeEventListener("resize", on); }, []);
  return w;
}
/** A pane size the user drags; remembered per browser, clamped to [min, max]. */
function usePaneSize(key: string, initial: number, min: number, max: number) {
  const [size, setSize] = useSticky<number>(key, initial);
  return { size: Math.min(max, Math.max(min, size)), set: (v: number) => setSize(Math.round(Math.min(max, Math.max(min, v)))) };
}
/** Drag handle between panes (mouse, pen and touch). onDrag gets the movement since the last event, in px. */
function Splitter({ axis, onDrag, className = "" }: { axis: "x" | "y"; onDrag: (delta: number) => void; className?: string }) {
  const last = useRef<number | null>(null);
  return (
    <div role="separator" aria-orientation={axis === "x" ? "vertical" : "horizontal"}
      className={`group relative z-10 shrink-0 touch-none select-none ${axis === "x" ? "w-1.5 cursor-col-resize" : "h-1.5 cursor-row-resize"} ${className}`}
      onPointerDown={(e) => { (e.target as HTMLElement).setPointerCapture(e.pointerId); last.current = axis === "x" ? e.clientX : e.clientY; }}
      onPointerMove={(e) => { if (last.current === null) return; const v = axis === "x" ? e.clientX : e.clientY; onDrag(v - last.current); last.current = v; }}
      onPointerUp={() => { last.current = null; }} onPointerCancel={() => { last.current = null; }}>
      <span className={`absolute rounded-full bg-line group-hover:bg-primary/60 ${axis === "x" ? "inset-y-0 left-1/2 w-0.5 -translate-x-1/2" : "inset-x-0 top-1/2 h-0.5 -translate-y-1/2"}`} />
    </div>
  );
}

export function Annotator({ ws, projectId, review, onExit }: { ws: string; projectId: string; review: boolean; onExit: () => void }) {
  const [item, setItem] = useState<LabelItem | null>(null);
  const [empty, setEmpty] = useState(false);
  const [ro, setRo] = useState(false);   // opened to look at (finished item): editing needs an explicit Edit
  const [claimed, setClaimed] = useState(false);   // the lock is taken only when work starts on the item (first change, submit, skip)
  const h = useHistory([]);
  const [etag, setEtag] = useState(0);
  const [saved, setSaved] = useState<Ann[]>([]);
  const [cls, setCls] = useSticky<number>(`label:${projectId}:class`, 0);
  const [sel, setSel] = useState<number | null>(null);
  const [text, setText] = useState("");
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [lastSubmit, setLastSubmit] = useState<HermesCheck | null>(null);
  const [filter, setFilter] = useSticky<string>(`label:${projectId}:filter`, review ? "in_review" : "");
  const [page, setPage] = useState(0);
  const [goto, setGoto] = useState("");
  const [panel, setPanel] = useState(false);   // phone: labels / AI / checks sheet
  // AI pre-labels: on opening an unlabeled item the first ready internal source proposes boxes (dashed, with confidence) — accept or delete.
  const [aiAuto, setAiAuto] = useSticky<boolean>(`label:${projectId}:ai-auto`, true);
  const [aiThr, setAiThr] = useSticky<number>(`label:${projectId}:ai-thr`, 0.3);
  const [aiBusy, setAiBusy] = useState(false);
  const [aiErr, setAiErr] = useState<string | null>(null);
  const asked = useRef<Set<string>>(new Set());
  const [aiPick, setAiPick] = useSticky<string>(`label:${projectId}:ai-source`, "");
  const [layerView, setLayerView] = useSticky<"all" | "human" | "ai">(`label:${projectId}:view`, "all");
  const [aiWaking, setAiWaking] = useState(false);
  const assist = useQuery({ queryKey: ["assist", ws, projectId], queryFn: () => api.labeling.getAssistSettings({ workspaceId: ws, projectId }),
    staleTime: 30_000, refetchInterval: aiWaking ? 5000 : false });
  const classes = useMemo(() => (item ? parseClasses(item.classesJson) : []), [item]);
  const stats = useQuery({ queryKey: ["label-stats", ws, projectId, "editor", item?.path ?? ""], queryFn: () => api.labeling.getProjectStats({ workspaceId: ws, projectId }) });
  const strip = useQuery({
    queryKey: ["label-strip", ws, projectId, filter, page], placeholderData: (x) => x,
    queryFn: () => api.labeling.listItems({ workspaceId: ws, projectId, status: filter === "mine" ? "" : filter, search: "", mine: filter === "mine", limit: STRIP, offset: page * STRIP }),
  });
  const rows = strip.data?.items ?? [];
  const total = Number(strip.data?.total ?? 0);
  const [guide, setGuide] = useState(false);
  const ref = item ? { workspaceId: ws, projectId, path: item.path } : null;
  const dirty = h.anns !== saved;
  const readOnly = review || ro;
  const media = item ? mediaOf(item.path) : "image";
  const idx = item ? rows.findIndex((r) => r.path === item.path) : -1;
  const done = Number(stats.data?.byStatus.accepted ?? 0n) + Number(stats.data?.byStatus.in_review ?? 0n);
  const all = Object.values(stats.data?.byStatus ?? {}).reduce((n, v) => n + Number(v), 0);

  const show = (next: LabelItem, look: boolean, mine = false) => {
    const a = fromItem(next);
    setItem(next); h.reset(a); setSaved(a); setEtag(next.etag); setSel(null); setText(""); setRo(look); setClaimed(mine); setEmpty(false);
  };
  /** Take the item's lock before the first write (browsing the strip never changes an item's status). */
  const claim = async (): Promise<number> => {
    if (!ref || claimed) return etag;
    const c = await api.labeling.claimItem(ref);
    setClaimed(true); setEtag(c.etag); setItem(c);
    return c.etag;
  };
  const save = useCallback(async () => {
    if (!ref || !dirty || readOnly) return etag;
    const snapshot = h.anns;
    let tag = await claim();
    const body = () => snapshot.map((a) => ({
      kind: a.kind, classId: a.classId, source: a.source || "human", attributesJson: "{}", confidence: a.confidence,
      geometryJson: JSON.stringify(a.geometry) }));
    let r;
    try { r = await api.labeling.saveAnnotations({ ...ref, etag: tag, annotations: body() }); }
    catch (e) {
      // The lock lapsed (item left open, or taken earlier): take it again and retry once.
      if (!/LOCK_EXPIRED|NOT_LOCKED|lock/i.test(errorText(e))) throw e;
      const c = await api.labeling.claimItem(ref);
      setClaimed(true); setItem(c); tag = c.etag;
      r = await api.labeling.saveAnnotations({ ...ref, etag: tag, annotations: body() });
    }
    setEtag(r.etag); setSaved(snapshot);
    return r.etag;
  }, [ref?.path, dirty, etag, h.anns, readOnly, claimed]); // eslint-disable-line react-hooks/exhaustive-deps

  /** Open one item: editable ones are claimed; finished ones open to look at (Edit claims them). The current one is saved first. */
  const open = useCallback(async (path: string, status?: string) => {
    setErr(null); setBusy(true);
    try {
      try { await save(); } catch { /* the draft stays on the server copy */ }
      const st = status ?? rows.find((r) => r.path === path)?.status ?? "unlabeled";
      const got = await api.labeling.getItem({ workspaceId: ws, projectId, path });
      const live = got.lockedByMe && !!got.lockExpiresAt && Number(got.lockExpiresAt.seconds) * 1000 > Date.now() + 5000;
      show(got, review || !EDITABLE.includes(st), live);
    } catch (e) { setErr(errorText(e)); } finally { setBusy(false); }
  }, [ws, projectId, review, rows, save]); // eslint-disable-line react-hooks/exhaustive-deps

  const load = useCallback(async () => {
    setErr(null); setBusy(true);
    try {
      const u = new URL(window.location.href), chosen = u.searchParams.get("path");
      if (chosen) { await open(chosen); return; }
      const next = (await (review ? api.labeling.nextReviewItem({ workspaceId: ws, projectId }) : api.labeling.nextItem({ workspaceId: ws, projectId }))).item;
      if (!next) { setEmpty(true); setItem(null); return; }
      show(next, false, true);
    } catch (e) { setErr(errorText(e)); } finally { setBusy(false); }
  }, [ws, projectId, review, open]); // eslint-disable-line react-hooks/exhaustive-deps
  const started = useRef(false);
  useEffect(() => { if (started.current) return; started.current = true; void load(); }, [load]);
  // ?path= set while the workspace is already open (link, WebMCP open_label_item): open that item.
  // The first one is opened by load(); the param is then dropped so the same item can be linked again.
  const linked = useRouterState({ select: (st) => new URLSearchParams(st.location.searchStr).get("path") });
  const nav = useNavigate();
  const seen = useRef(linked);
  useEffect(() => {
    if (linked && linked !== seen.current) void open(linked);
    seen.current = linked;
    if (linked) void nav({ to: ".", replace: true, search: (s: Record<string, unknown>) => { const { path: _drop, ...rest } = s; return rest; } } as never);
  }, [linked]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { if (classes.length && !classes.some((c) => c.id === cls)) setCls(classes[0].id); }, [classes, cls, setCls]);

  // Auto-save 1 s after the last change; renew the lock every 60 s while an editable item is open.
  useEffect(() => { if (!dirty || readOnly) return undefined; const x = setTimeout(() => save().catch((e) => setErr(errorText(e))), 1000); return () => clearTimeout(x); }, [dirty, save, readOnly]);
  useEffect(() => {
    if (!ref || readOnly || !claimed) return undefined;
    const x = setInterval(async () => { try { await api.labeling.heartbeatLock(ref); } catch (e) { setErr(errorText(e)); } }, 60000);
    return () => clearInterval(x);
  }, [ref?.path, readOnly, claimed]); // eslint-disable-line react-hooks/exhaustive-deps

  // Repeated arrow presses while an item is still loading keep counting from the target, not from the item on screen.
  const target = useRef<number | null>(null);
  const step = (d: 1 | -1) => {
    const base = target.current ?? idx;
    if (base >= 0 && rows[base + d]) {
      target.current = base + d;
      const r = rows[base + d];
      return void open(r.path, r.status).finally(() => { if (target.current === base + d) target.current = null; });
    }
    if (d > 0 && (page + 1) * STRIP < total) setPage(page + 1);
    if (d < 0 && page > 0) setPage(page - 1);
  };
  /** After submit / skip / accept: the next item of the strip that still needs work, else the server queue. */
  const advance = async () => {
    void strip.refetch(); void stats.refetch();
    const want = review ? ["in_review"] : EDITABLE;
    const after = rows.slice(Math.max(0, idx + 1)).find((r) => want.includes(r.status) && r.path !== item?.path);
    if (after) await open(after.path, after.status); else await load();
  };
  async function act(kind: "submit" | "skip" | "accept" | "reject") {
    if (!ref) return;
    setErr(null); setBusy(true);
    try {
      if (!review) await claim();
      if (kind === "submit") { await save(); const r = await api.labeling.submitItem(ref); setLastSubmit(r.hermes ?? null); }
      if (kind === "skip") await api.labeling.skipItem({ ...ref, text });
      if (kind === "accept") await api.labeling.acceptItem({ ...ref, text });
      if (kind === "reject") await api.labeling.rejectItem({ ...ref, text });
      await advance();
    } catch (e) { setErr(errorText(e)); setBusy(false); }
  }
  const edit = async () => { if (item) { try { show(await api.labeling.claimItem({ workspaceId: ws, projectId, path: item.path }), false, true); } catch (e) { setErr(errorText(e)); } } };
  const jump = () => {
    const n = Number(goto);
    if (Number.isInteger(n) && n >= 1 && n <= total) {
      const p = Math.floor((n - 1) / STRIP);
      if (p === page && rows[(n - 1) % STRIP]) void open(rows[(n - 1) % STRIP].path, rows[(n - 1) % STRIP].status); else setPage(p);
    } else { const r = rows.find((x) => x.path.includes(goto)); if (r) void open(r.path, r.status); }
    setGoto("");
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;
      const mod = e.ctrlKey || e.metaKey;
      if (mod && e.key.toLowerCase() === "z") { e.preventDefault(); if (!readOnly) { if (e.shiftKey) h.redo(); else h.undo(); } return; }
      if (mod && e.key.toLowerCase() === "y") { e.preventDefault(); if (!readOnly) h.redo(); return; }
      if (mod && e.key.toLowerCase() === "s") { e.preventDefault(); save().catch((x) => setErr(errorText(x))); return; }
      if (e.key === "ArrowRight" && !mod) { e.preventDefault(); step(1); return; }
      if (e.key === "ArrowLeft" && !mod) { e.preventDefault(); step(-1); return; }
      if (e.key === "Enter" && !e.shiftKey && !busy) { e.preventDefault(); void act(review ? "accept" : "submit"); return; }
      const k = e.key.toLowerCase();
      if (!mod && k === "e" && ro && !review) { void edit(); return; }
      if (!mod && k === "q") { setLayerView(layerView === "all" ? "human" : layerView === "human" ? "ai" : "all"); return; }
      if (!mod && k === "a" && !readOnly && !review) { if (e.shiftKey) acceptAi(); else if (sel !== null && h.anns[sel]?.source === "model") acceptAi(sel); return; }
      if (!mod && k === "s" && !review && text.trim()) { void act("skip"); return; }
      if (!mod && (k === "s" || k === "r")) { document.getElementById("label-note")?.focus(); e.preventDefault(); return; }
      const n = Number(e.key);
      if (!mod && n >= 1 && n <= 9 && classes[n - 1]) {
        setCls(classes[n - 1].id);
        if (sel !== null && !readOnly) h.set((as) => as.map((a, i) => (i === sel ? { ...a, classId: classes[n - 1].id } : a)));
      } else if ((e.key === "Delete" || e.key === "Backspace") && sel !== null && !readOnly) {
        h.set((as) => as.filter((_, i) => i !== sel)); setSel(null);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  // Keep the open item visible in the strip.
  const stripRef = useRef<HTMLDivElement>(null);
  useEffect(() => { stripRef.current?.querySelector('[aria-current="true"]')?.scrollIntoView({ block: "nearest", inline: "center" }); }, [item?.path, rows.length]);

  // The user chooses the source and turns a stopped deployment on only when needed (saves GPU); nothing starts by itself.
  const aiChoices = (assist.data?.sources ?? []).filter((x) => x.enabled && !x.external && x.media.includes(media));
  const aiChosen = aiChoices.find((x) => x.id === aiPick) ?? aiChoices.find((x) => x.state === "ready") ?? aiChoices[0];
  const aiSource = aiChosen && aiChosen.state !== "unavailable" ? aiChosen : undefined;
  useEffect(() => { if (aiWaking && aiChosen?.state === "ready") setAiWaking(false); }, [aiWaking, aiChosen?.state]);
  const aiPower = async (on: boolean) => {
    if (!aiChosen || aiChosen.kind !== "deployment") return;
    setAiErr(null);
    try {
      if (on) {
        const start = () => api.serving.startDeployment({ workspaceId: ws, deploymentId: aiChosen.id, modelVersionId: "" });
        // A deployment stuck between states (its start failed before a replica ran) is stopped first, then started.
        try { await start(); } catch (e) { if (!/SERVE_RUNNING/.test(errorText(e))) throw e; await api.serving.stopDeployment({ workspaceId: ws, deploymentId: aiChosen.id }); await start(); }
        setAiWaking(true);
      }
      else { await api.serving.stopDeployment({ workspaceId: ws, deploymentId: aiChosen.id }); }
      void assist.refetch();
    } catch (e) { setAiErr(errorText(e)); }
  };
  const askAi = async () => {
    if (!item || !aiSource) return;
    setAiBusy(true); setAiErr(null);
    try {
      const set = await api.labeling.proposeItem({ workspaceId: ws, projectId, path: item.path, source: { kind: aiSource.kind, id: aiSource.id }, confirmExternal: false });
      const add = toAnns(set.items).filter((a) => (a.confidence ?? 1) >= aiThr);
      if (set.state === "failed") setAiErr(set.detail || t("failed")); else if (set.unmapped.length && !set.items.length) setAiErr(`${t("Unmapped")}: ${set.unmapped.map((u) => u.label).join(", ")}`);
      if (add.length) h.set((as) => [...as.filter((a) => a.source !== "model"), ...add]);
      void api.labeling.decideSuggestionSet({ workspaceId: ws, projectId, id: set.id, decision: add.length ? "applied" : "dismissed" }).catch(() => undefined);
    } catch (e) { setAiErr(errorText(e)); } finally { setAiBusy(false); }
  };
  useEffect(() => {
    if (!item || readOnly || !aiAuto || !aiSource || item.annotations.length > 0 || asked.current.has(item.path)) return;
    asked.current.add(item.path);
    void askAi();
  }, [item?.path, readOnly, aiAuto, aiSource?.id]); // eslint-disable-line react-hooks/exhaustive-deps
  const aiCount = h.anns.filter((a) => a.source === "model").length;
  const agree = useMemo(() => agreement(h.anns), [h.anns]);
  /** Accept AI boxes: the selected one, or all at/above the threshold (the rest are removed). Accepted boxes become solid ("ai"). */
  const acceptAi = (only?: number) => h.set((as) => as
    .filter((a, i) => a.source !== "model" || only !== undefined ? true : (a.confidence ?? 1) >= aiThr || i < 0)
    .map((a, i) => (a.source === "model" && (only === undefined || i === only) ? { ...a, source: "ai" } : a)));

  const hasForeignDraft = item && item.draftJson && !dirty && item.draftBy !== item.annotatorId && !readOnly;
  const editorProps: EditorProps = { item: item!, anns: h.anns, set: h.set, sel, setSel, cls, classes, readOnly, view: layerView, match: agree.ai && agree.human ? agree.match : undefined };
  const FILTERS: [string, string][] = review ? [["in_review", t("In review")], ["accepted", t("accepted")], ["", t("All")]]
    : [["", t("All")], ["unlabeled", t("unlabeled")], ["mine", t("Mine")], ["in_review", t("In review")], ["returned", t("returned")], ["accepted", t("accepted")]];

  // Layout: the strip sits on the left on wide windows and at the bottom on narrow ones (or where the user pinned it); every pane is dragged to size.
  const winW = useWindowWidth();
  const [stripPos, setStripPos] = useSticky<"auto" | "side" | "bottom">("label:strip-pos", "auto");
  const stripSide = stripPos === "side" || (stripPos === "auto" && winW >= 1100);
  const stripW = usePaneSize("label:strip-w", 240, 120, 640);
  const stripH = usePaneSize("label:strip-h", 120, 80, 420);
  const panelW = usePaneSize("label:panel-w", 288, 220, 560);
  const stripBody = (vertical: boolean) => (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex shrink-0 items-center gap-1.5 overflow-x-auto px-2 pt-2 [scrollbar-width:none]">
        {FILTERS.map(([k, label]) => (
          <button key={k} onClick={() => { setFilter(k); setPage(0); }}
            className={`h-8 shrink-0 whitespace-nowrap rounded-full px-3 text-xs ${filter === k ? "bg-primary/15 font-medium text-primary" : "text-fg-muted hover:bg-surface-2"}`}>{label}</button>))}
      </div>
      <div className="flex shrink-0 items-center gap-1 px-2 py-1 text-xs text-fg-subtle">
        <span className="tabular-nums">{total > STRIP ? `${page * STRIP + 1}–${Math.min(total, (page + 1) * STRIP)} / ${total}` : total}</span>
        {total > STRIP && <>
          <button className="h-7 w-7 rounded hover:bg-surface-2 disabled:opacity-40" disabled={page === 0} onClick={() => setPage(page - 1)} aria-label="«"><ChevronLeft className="mx-auto h-4 w-4" /></button>
          <button className="h-7 w-7 rounded hover:bg-surface-2 disabled:opacity-40" disabled={(page + 1) * STRIP >= total} onClick={() => setPage(page + 1)} aria-label="»"><ChevronRight className="mx-auto h-4 w-4" /></button>
        </>}
        <button className="ml-auto hidden h-7 w-7 items-center justify-center rounded hover:bg-surface-2 md:inline-flex" aria-label={t("Strip position")}
          onClick={() => setStripPos(stripSide ? "bottom" : "side")}>{stripSide ? <PanelBottom className="h-4 w-4" /> : <PanelLeft className="h-4 w-4" />}</button>
      </div>
      <div ref={stripRef} className={vertical ? "grid min-h-0 flex-1 auto-rows-max grid-cols-[repeat(auto-fill,minmax(96px,1fr))] gap-1.5 overflow-y-auto p-2" : "flex min-h-0 flex-1 gap-1.5 overflow-x-auto px-2 pb-2"}>
        {rows.map((r, i) => {
          const on = r.path === item?.path;
          return (
            <button key={r.path} aria-current={on} title={r.path} onClick={() => void open(r.path, r.status)}
              className={`relative shrink-0 overflow-hidden rounded-md border-2 bg-surface-2 ${vertical ? "aspect-[4/3] w-full" : "aspect-[4/3] h-full"} ${on ? "border-primary" : "border-transparent hover:border-line"}`}>
              {r.url && mediaOf(r.path) === "image" ? <img src={r.url} alt="" loading="lazy" className="h-full w-full object-cover" />
                : <span className="mono block truncate p-1 text-[10px] text-fg-muted">{r.path.split("/").pop()}</span>}
              <span className={`absolute bottom-1 left-1 h-2.5 w-2.5 rounded-full ring-2 ring-surface ${STATUS_DOT[r.status] ?? "bg-fg-subtle"}`} />
              {Number(r.labels) > 0 && <span className="absolute right-1 top-1 rounded bg-black/60 px-1 text-[10px] tabular-nums text-white">{Number(r.labels)}</span>}
              <span className="absolute bottom-0.5 right-1 text-[10px] tabular-nums text-white [text-shadow:0_0_3px_#000]">{page * STRIP + i + 1}</span>
            </button>);
        })}
        {strip.isLoading && Array.from({ length: 8 }, (_, i) => <div key={i} className={`skeleton shrink-0 ${vertical ? "aspect-[4/3] w-full" : "aspect-[4/3] h-full"}`} />)}
      </div>
    </div>
  );

  const aiBlock = !readOnly && media === "image" && (
    <div className="space-y-2 rounded-lg border border-primary/30 bg-primary/5 p-2.5">
      <div className="flex items-center gap-2 text-xs font-medium">
        <Wand2 className="h-3.5 w-3.5 text-primary" />AI
        <label className="ml-auto inline-flex items-center gap-1.5 font-normal text-fg-muted"><input type="checkbox" checked={aiAuto} onChange={(e) => setAiAuto(e.target.checked)} />{t("Auto")}</label>
      </div>
      {aiChoices.length === 0 ? <div className="text-xs text-warning">{t("No AI source ready")}</div> : (
        <div className="flex items-center gap-1.5">
          <select className="input h-9 min-w-0 flex-1 text-xs" value={aiChosen?.id ?? ""} onChange={(e) => setAiPick(e.target.value)} aria-label={t("AI source")}>
            {aiChoices.map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}
          </select>
          {aiChosen?.kind === "deployment" && (aiChosen.state === "ready"
            ? <button className="btn-ghost h-9 shrink-0 px-2 text-xs" onClick={() => void aiPower(false)}><Power className="h-3.5 w-3.5" />{t("Turn off")}</button>
            : <button className="btn-primary h-9 shrink-0 px-2 text-xs" disabled={aiWaking} onClick={() => void aiPower(true)}>{aiWaking ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Power className="h-3.5 w-3.5" />}{aiWaking ? t("Starting…") : t("Turn on")}</button>)}
        </div>)}
      {aiSource && <>
        <label className="flex items-center gap-2 text-xs text-fg-muted">{t("Threshold")}
          <input type="range" min={0.05} max={0.95} step={0.05} value={aiThr} onChange={(e) => setAiThr(Number(e.target.value))} className="flex-1" />
          <span className="w-9 text-right tabular-nums text-fg">{aiThr.toFixed(2)}</span></label>
        <div className="flex gap-1.5">
          <button className="btn-ghost h-9 flex-1 px-2 text-xs" disabled={aiBusy || !item} onClick={() => void askAi()}>{aiBusy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Wand2 className="h-3.5 w-3.5" />}{t("Ask AI")}</button>
          <button className="btn-primary h-9 flex-1 px-2 text-xs" disabled={aiCount === 0} onClick={() => acceptAi()}><CheckCheck className="h-3.5 w-3.5" />{t("Accept")} {aiCount} ⇧A</button>
        </div>
      </>}
      {aiErr && <div className="break-words text-xs text-danger">{aiErr}</div>}
      <div className="inline-flex w-full rounded-lg border border-line p-0.5 text-xs" role="tablist" aria-label={t("Show")}>
        {([["all", t("All")], ["human", t("People")], ["ai", "AI"]] as const).map(([k, l]) => (
          <button key={k} role="tab" aria-selected={layerView === k} onClick={() => setLayerView(k)}
            className={`h-8 flex-1 rounded-md ${layerView === k ? "bg-primary/15 font-medium text-primary" : "text-fg-muted"}`}>{l}</button>))}
        <span className="self-center px-1.5 text-[10px] text-fg-subtle">Q</span>
      </div>
      {agree.ai > 0 && agree.human > 0 && (
        <div className="grid grid-cols-2 gap-x-3 gap-y-1 rounded-md bg-surface-2 p-2 text-xs tabular-nums">
          <span className="text-fg-muted">{t("Matched")}</span><span className={agree.matched === agree.ai ? "text-success" : "text-warning"}>{agree.matched} / {agree.ai}</span>
          <span className="text-fg-muted">{t("Mean IoU")}</span><span>{agree.matched ? agree.meanIou.toFixed(2) : "—"}</span>
          <span className="text-fg-muted">{t("Wrong class")}</span><span className={agree.wrongClass ? "text-danger" : ""}>{agree.wrongClass}</span>
          <span className="text-fg-muted">{t("AI only")}</span><span>{agree.aiOnly}</span>
          <span className="text-fg-muted">{t("People only")}</span><span>{agree.humanOnly}</span>
        </div>)}
    </div>);
  const panelBody = (<>
    {aiBlock}
            <ErrorBox error={err} />
            {hasForeignDraft && (
              <div className="flex items-center gap-2 rounded-lg border border-warning/30 bg-warning/10 p-2 text-xs text-warning">
                {t("Draft")} {item!.draftSavedAt ? ago(item!.draftSavedAt) : ""}
                <button className="ml-auto underline" onClick={() => h.set(fromDraft(item!.draftJson))}>{t("Use draft")}</button>
              </div>)}
            <Section title={t("Classes")}>
              {classes.map((c, i) => (
                <button key={c.id} className={`flex h-9 w-full items-center gap-2 rounded px-2 text-left ${cls === c.id ? "bg-primary/15 text-fg" : "text-fg-muted hover:bg-surface-2"}`} onClick={() => setCls(c.id)}>
                  <span className="h-3 w-3 rounded-sm" style={{ background: c.color }} />{c.name}
                  <span className="ml-auto text-xs text-fg-subtle">{h.anns.filter((a) => a.classId === c.id).length} · {i + 1}</span>
                </button>))}
            </Section>
            <Layers anns={h.anns} classes={classes} sel={sel} setSel={setSel} set={h.set} readOnly={readOnly} />
            {item && !readOnly && <AssistPanel key={"assist" + item.path} ws={ws} projectId={projectId} path={item.path} media={media} classesJson={item.classesJson} readOnly={readOnly}
              onApply={(add: ProposedAnn[]) => h.set((as) => {
                let out = [...as];
                for (const a of add) {
                  if (a.kind === "tag" && out.some((x) => x.kind === "tag" && x.classId === a.classId)) continue;
                  if (a.kind === "transcript") out = out.filter((x) => x.kind !== "transcript");
                  out.push(a);
                }
                return out;
              })} />}
            {item && <HermesPanel key={item.path} ws={ws} projectId={projectId} path={item.path} readOnly={readOnly} beforeCheck={save}
              lastSubmit={lastSubmit} onLastChanged={setLastSubmit} onDismissLast={() => setLastSubmit(null)} etag={etag} dirty={dirty} />}
            {(item?.comments.length ?? 0) > 0 && (
              <Section title={t("Comments")}>
                {item!.comments.map((c) => <div key={c.id} className="mb-1 rounded bg-surface-2 p-2 text-xs"><b>{c.author}</b>: {c.body}</div>)}
              </Section>)}
          
  </>);
  const actionsBody = (<>
            <textarea id="label-note" className="input h-14" placeholder={review ? t("Comment") : t("Reason")} value={text} onChange={(e) => setText(e.target.value)} />
            {review ? (
              <div className="flex gap-2">
                <button className="btn-primary h-10 flex-1" disabled={busy || !item} onClick={() => void act("accept")}>{t("Accept")} ⏎</button>
                <button className="btn-ghost h-10 flex-1" disabled={busy || !item || !text.trim()} onClick={() => void act("reject")}>{t("Return")}</button>
              </div>
            ) : (
              <div className="flex gap-2">
                <button className="btn-primary h-10 flex-1" disabled={busy || !item || ro} onClick={() => void act("submit")}><Send className="h-4 w-4" />{t("Submit")} ⏎</button>
                <button className="btn-ghost h-10 flex-1" disabled={busy || !item || ro || !text.trim()} onClick={() => void act("skip")}><SkipForward className="h-4 w-4" />{t("Skip")}</button>
              </div>)}
</>);

  return (
    <div className="fixed inset-x-0 top-0 z-40 flex h-[100dvh] flex-col bg-bg">
      <header className="flex h-12 shrink-0 items-center gap-2 border-b border-line bg-surface px-2 text-sm sm:gap-3 sm:px-3">
        <button className="inline-flex h-9 items-center gap-1.5 rounded-md px-2 text-fg-muted hover:bg-surface-2 hover:text-fg"
          onClick={async () => { try { await save(); } catch { /* keep the draft */ } onExit(); }} aria-label={t("Exit")}><ArrowLeft className="h-4 w-4" /><span className="hidden sm:inline">{t("Exit")}</span></button>
        {all > 0 && (
          <div className="hidden min-w-0 items-center gap-2 md:flex" title={`${done}/${all}`}>
            <div className="flex h-2 w-40 overflow-hidden rounded-full bg-surface-2">
              <div className="bg-success" style={{ width: `${(Number(stats.data?.byStatus.accepted ?? 0n) / all) * 100}%` }} />
              <div className="bg-warning" style={{ width: `${(Number(stats.data?.byStatus.in_review ?? 0n) / all) * 100}%` }} />
            </div>
            <span className="text-xs tabular-nums text-fg-muted">{done}/{all}</span>
          </div>)}
        <div className="flex items-center gap-1">
          <button className="inline-flex h-9 w-9 items-center justify-center rounded-md hover:bg-surface-2 disabled:opacity-40" disabled={busy || (idx <= 0 && page === 0)} onClick={() => step(-1)} aria-label="←"><ChevronLeft className="h-5 w-5" /></button>
          <span className="min-w-[5rem] text-center text-xs tabular-nums">{idx >= 0 ? `${page * STRIP + idx + 1} / ${total}` : `— / ${total}`}</span>
          <button className="inline-flex h-9 w-9 items-center justify-center rounded-md hover:bg-surface-2 disabled:opacity-40" disabled={busy} onClick={() => step(1)} aria-label="→"><ChevronRight className="h-5 w-5" /></button>
        </div>
        <form className="hidden sm:block" onSubmit={(e) => { e.preventDefault(); jump(); }}>
          <input className="input h-9 w-32 text-xs" placeholder={t("Go to")} value={goto} onChange={(e) => setGoto(e.target.value)} aria-label={t("Go to")} />
        </form>
        <span className="mono hidden min-w-0 truncate text-xs text-fg-muted lg:inline">{item?.path ?? ""}</span>
        {item && <span className="hidden sm:inline"><StateBadge state={item.status} /></span>}
        <span className="ml-auto" />
        {ro && !review && <button className="btn-ghost hidden h-9 gap-1.5 px-3 md:inline-flex" onClick={() => void edit()}><Pencil className="h-4 w-4" />{t("Edit")}</button>}
        {stats.data?.project?.guidelineMd && <button className="btn-ghost hidden h-9 w-9 p-0 sm:inline-flex" aria-label={t("Instructions")} onClick={() => setGuide(true)}><BookOpen className="h-4 w-4" /></button>}
        {guide && <Dialog title={t("Instructions")} description={stats.data?.project?.name} onClose={() => setGuide(false)}><div className="whitespace-pre-wrap text-sm text-fg-muted">{stats.data?.project?.guidelineMd}</div></Dialog>}
        {!readOnly && <>
          <button className="btn-ghost h-9 w-9 p-0" aria-label={t("Undo (Ctrl+Z)")} disabled={!h.canUndo} onClick={h.undo}><Undo2 className="h-4 w-4" /></button>
          <button className="btn-ghost hidden h-9 w-9 p-0 sm:inline-flex" aria-label={t("Redo (Ctrl+Shift+Z)")} disabled={!h.canRedo} onClick={h.redo}><Redo2 className="h-4 w-4" /></button>
          <span className={`hidden items-center gap-1 text-xs sm:inline-flex ${dirty ? "text-fg-muted" : "text-success"}`}><Save className="h-3.5 w-3.5" />{dirty ? t("Saving…") : t("Saved")}</span>
        </>}
      </header>
      <div className="flex min-h-0 flex-1">
        {stripSide && <>
          <div className="hidden shrink-0 flex-col border-r border-line bg-surface md:flex" style={{ width: stripW.size }}>{stripBody(true)}</div>
          <Splitter axis="x" onDrag={(d) => stripW.set(stripW.size + d)} />
        </>}
        <div className="flex min-w-0 flex-1 flex-col">
          <div className="relative min-h-0 flex-1">
            {empty ? (
              <div className="flex h-full flex-col items-center justify-center gap-3 text-fg-muted">
                <div>{review ? t("No items left to review.") : t("All done — no items left to label.")}</div>
                <button className="btn-ghost" onClick={() => void load()}>{t("Check again")}</button>
                {lastSubmit && <div className="w-full max-w-md"><LastSubmitCard ws={ws} projectId={projectId} check={lastSubmit} onChange={setLastSubmit} onDismiss={() => setLastSubmit(null)} /></div>}
              </div>
            ) : item ? (media === "image" ? <ImageEditor {...editorProps} />
              : media === "text" ? <TextEditor {...editorProps} />
              : media === "audio" ? <AudioEditor {...editorProps} />
              : <VideoEditor {...editorProps} />)
              : <div className="flex h-full items-center justify-center text-fg-muted">{busy ? <div className="skeleton h-8 w-48" /> : null}</div>}
          </div>
          {!stripSide && <>
            <Splitter axis="y" onDrag={(d) => stripH.set(stripH.size - d)} />
            <div className="shrink-0 border-t border-line bg-surface" style={{ height: stripH.size }}>{stripBody(false)}</div>
          </>}
        </div>
        <Splitter axis="x" className="hidden md:block" onDrag={(d) => panelW.set(panelW.size - d)} />
        <aside className="hidden shrink-0 flex-col border-l border-line bg-surface text-sm md:flex" style={{ width: panelW.size }}>
          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-3">{panelBody}</div>
          <div className="space-y-2 border-t border-line p-3">{actionsBody}</div>
        </aside>
      </div>
      {/* Phone: classes and actions at the thumb; labels / AI / checks in a sheet */}
      <div className="shrink-0 space-y-2 border-t border-line bg-surface p-2 md:hidden">
        {err && <ErrorBox error={err} />}
        <div className="flex gap-1.5 overflow-x-auto [scrollbar-width:none]">
          {classes.map((c, i) => (
            <button key={c.id} onClick={() => { setCls(c.id); if (sel !== null && !readOnly) h.set((as) => as.map((x, j) => (j === sel ? { ...x, classId: c.id } : x))); }}
              className={`flex h-10 shrink-0 items-center gap-2 rounded-full border px-3 text-sm ${cls === c.id ? "border-primary bg-primary/15 text-fg" : "border-line text-fg-muted"}`}>
              <span className="h-3 w-3 rounded-sm" style={{ background: c.color }} />{c.name}<span className="text-xs text-fg-subtle">{h.anns.filter((x) => x.classId === c.id).length}</span>
              <span className="sr-only">{i + 1}</span>
            </button>))}
        </div>
        <div className="flex gap-2">
          <button className="btn-ghost h-11 flex-1 gap-1.5" onClick={() => setPanel(true)}><Tag className="h-4 w-4" />{t("Labels")} {h.anns.length}</button>
          {ro && !review ? <button className="btn-ghost h-11 flex-1 gap-1.5" onClick={() => void edit()}><Pencil className="h-4 w-4" />{t("Edit")}</button>
            : review ? <button className="btn-primary h-11 flex-1" disabled={busy || !item} onClick={() => void act("accept")}>{t("Accept")}</button>
            : <button className="btn-primary h-11 flex-1 gap-1.5" disabled={busy || !item} onClick={() => void act("submit")}><Send className="h-4 w-4" />{t("Submit")}</button>}
        </div>
      </div>
      {panel && <Dialog title={t("Labels")} onClose={() => setPanel(false)}><div className="space-y-4 text-sm">{panelBody}<div className="space-y-2 border-t border-line pt-3">{actionsBody}</div></div></Dialog>}
    </div>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return <div><div className="mb-1.5 text-[11px] font-semibold uppercase tracking-wider text-fg-subtle">{title}</div>{children}</div>;
}

const KIND_LABEL: Record<string, string> = { bbox: "box", polygon: "polygon", mask: "mask", keypoints: "keypoints", tag: "tag", span: "span", segment: "segment", transcript: "transcript" };

function Layers({ anns, classes, sel, setSel, set, readOnly }:
  { anns: Ann[]; classes: Cls[]; sel: number | null; setSel: (i: number | null) => void; set: (f: (a: Ann[]) => Ann[]) => void; readOnly: boolean }) {
  return (
    <Section title={t("Labels ({n})", { n: anns.length })}>
      {anns.length === 0 && <div className="text-xs text-fg-subtle">{t("No labels yet.")}</div>}
      {anns.map((a, i) => {
        const c = classes.find((x) => x.id === a.classId);
        return (
          <div key={i} className={`group flex items-center gap-2 rounded px-2 py-1 ${sel === i ? "bg-surface-2" : "hover:bg-surface-2/60"}`} onClick={() => setSel(i)}>
            <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ background: c?.color ?? "#64748b" }} />
            <span className="min-w-0 flex-1 truncate">{a.kind === "transcript" ? t("Transcript") : c?.name ?? a.classId}
              <span className="ml-1 text-xs text-fg-subtle">{t(KIND_LABEL[a.kind] ?? a.kind)}</span></span>
            {a.source === "model" && <span className="badge bg-violet-100 text-violet-700">AI{a.confidence !== undefined ? ` ${Math.round(a.confidence * 100)}%` : ""}</span>}
            <button className="text-fg-subtle hover:text-fg" onClick={(e) => { e.stopPropagation(); set((as) => as.map((x, j) => (j === i ? { ...x, hidden: !x.hidden } : x))); }}
              aria-label={a.hidden ? t("Show") : t("Hide")}>{a.hidden ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}</button>
            {!readOnly && <button className="text-fg-subtle hover:text-danger" onClick={(e) => { e.stopPropagation(); set((as) => as.filter((_, j) => j !== i)); setSel(null); }}
              aria-label={t("Delete")}><Trash2 className="h-3.5 w-3.5" /></button>}
          </div>);
      })}
    </Section>
  );
}

type EditorProps = {
  item: LabelItem; anns: Ann[]; set: (f: Ann[] | ((a: Ann[]) => Ann[])) => void; sel: number | null; setSel: (i: number | null) => void;
  cls: number; classes: Cls[]; readOnly: boolean;
  /** which labels are drawn: everything, only people's, only AI proposals (hides the rest without changing them) */
  view?: "all" | "human" | "ai";
  /** per label index: best overlap with the other side (AI ↔ person) */
  match?: Map<number, { iou: number; same: boolean }>;
};
const colorOf = (classes: Cls[], id: number) => classes.find((c) => c.id === id)?.color ?? "#64748b";

/** Image-level / item-level classification tags (kind "tag"). */
function TagBar({ anns, set, classes, readOnly }: Pick<EditorProps, "anns" | "set" | "classes" | "readOnly">) {
  const on = (id: number) => anns.some((a) => a.kind === "tag" && a.classId === id);
  const toggle = (id: number) => set((as) => (on(id) ? as.filter((a) => !(a.kind === "tag" && a.classId === id)) : [...as, { kind: "tag", classId: id, geometry: {}, source: "human" }]));
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <Tag className="h-4 w-4 text-fg-subtle" />
      {classes.map((c) => (
        <button key={c.id} disabled={readOnly} onClick={() => toggle(c.id)}
          className={`rounded-full border px-2.5 py-0.5 text-xs ${on(c.id) ? "text-white" : "border-line text-fg-muted hover:text-fg"}`}
          style={on(c.id) ? { background: c.color, borderColor: c.color } : undefined}>{c.name}</button>))}
    </div>
  );
}

/* ═════════════════════════════ Image ═════════════════════════════ */

type Tool = "select" | "pan" | "box" | "polygon" | "brush" | "erase" | "keypoints";
const TOOLS: [Tool, string, typeof Square, string][] = [
  ["select", "Select / move", MousePointer2, "V"], ["pan", "Pan", Hand, "H"], ["box", "Box", Square, "B"], ["polygon", "Polygon", Pentagon, "P"],
  ["brush", "Brush (mask)", Brush, "M"], ["erase", "Eraser (mask)", Eraser, "E"], ["keypoints", "Keypoints", Circle, "K"],
];

function ImageEditor({ item, anns, set, sel, setSel, cls, classes, readOnly, view: layer = "all", match }: EditorProps) {
  const [tool, setTool] = useSticky<Tool>("label:tool", "box");
  const [brushSize, setBrushSize] = useSticky("label:brush", 24);
  const [view, setView] = useState({ s: 1, x: 0, y: 0 });
  const [nat, setNat] = useState({ w: 0, h: 0 });
  const [drag, setDrag] = useState<{ x0: number; y0: number; x1: number; y1: number } | null>(null);
  const [poly, setPoly] = useState<[number, number][]>([]);
  const [kps, setKps] = useState<[number, number, number][]>([]);
  const [move, setMove] = useState<{ i: number; px: number; py: number; orig: Ann; handle?: string } | null>(null);
  const [panning, setPanning] = useState<{ x: number; y: number; vx: number; vy: number } | null>(null);
  const [space, setSpace] = useState(false);
  const wrap = useRef<HTMLDivElement>(null);
  const stage = useRef<HTMLDivElement>(null);
  const paint = useRef<HTMLCanvasElement>(null);
  const painting = useRef(false);
  const [cross, setCross] = useState<[number, number] | null>(null);

  // Fit the image into the viewport whenever a new item loads.
  const fit = useCallback(() => {
    const el = wrap.current;
    if (!el || !nat.w) return;
    const s = Math.min((el.clientWidth - 8) / nat.w, (el.clientHeight - 8) / nat.h, 4);
    setView({ s, x: (el.clientWidth - nat.w * s) / 2, y: (el.clientHeight - nat.h * s) / 2 });
  }, [nat]);
  useEffect(() => { fit(); }, [fit, item.path]);
  // Refit when the canvas area changes size (window, side panel, strip).
  useEffect(() => {
    const el = wrap.current;
    if (!el || typeof ResizeObserver === "undefined") return undefined;
    const ro = new ResizeObserver(() => fit());
    ro.observe(el);
    return () => ro.disconnect();
  }, [fit]);
  useEffect(() => { setPoly([]); setKps([]); setDrag(null); }, [item.path]);

  // Tool keys + space-to-pan.
  useEffect(() => {
    const down = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || e.ctrlKey || e.metaKey) return;
      if (e.key === " ") { setSpace(true); e.preventDefault(); return; }
      const k = e.key.toUpperCase();
      const hit = TOOLS.find((x) => x[3] === k);
      if (hit && !readOnly) setTool(hit[0]);
      if (k === "F") fit();
      if (e.key === "Escape") { setPoly([]); setKps([]); setDrag(null); setSel(null); }
      if (e.key === "Enter" && !e.ctrlKey) { finishPoly(); finishKps(); }
    };
    const up = (e: KeyboardEvent) => { if (e.key === " ") setSpace(false); };
    window.addEventListener("keydown", down); window.addEventListener("keyup", up);
    return () => { window.removeEventListener("keydown", down); window.removeEventListener("keyup", up); };
  });

  const toImg = (e: React.MouseEvent | React.WheelEvent) => {
    const r = stage.current!.getBoundingClientRect();
    return { x: (e.clientX - r.left) / r.width, y: (e.clientY - r.top) / r.height };
  };
  const onWheel = (e: React.WheelEvent) => {
    const r = wrap.current!.getBoundingClientRect();
    const mx = e.clientX - r.left, my = e.clientY - r.top;
    // Smooth zoom proportional to the scroll amount (trackpads send many small events; pinch arrives as ctrl+wheel), at most ±10% per event.
    const px = e.deltaMode === 1 ? e.deltaY * 16 : e.deltaMode === 2 ? e.deltaY * 400 : e.deltaY;
    const f = Math.min(1.1, Math.max(1 / 1.1, Math.exp(-px * (e.ctrlKey ? 0.006 : 0.0012))));
    const s = Math.min(20, Math.max(0.05, view.s * f));
    setView({ s, x: mx - ((mx - view.x) / view.s) * s, y: my - ((my - view.y) / view.s) * s });
  };

  const finishPoly = () => {
    if (poly.length >= 3) { set((as) => [...as, { kind: "polygon", classId: cls, geometry: { points: poly.map(([x, y]) => [r4(x), r4(y)]) }, source: "human" }]); }
    setPoly([]);
  };
  const finishKps = () => {
    if (kps.length > 0) { set((as) => [...as, { kind: "keypoints", classId: cls, geometry: { points: kps.map(([x, y, v]) => [r4(x), r4(y), v]) }, source: "human" }]); }
    setKps([]);
  };

  // ── mask painting: a working canvas at (capped) image resolution for the selected/current-class mask
  const maskIndex = useMemo(() => {
    if (sel !== null && anns[sel]?.kind === "mask") return sel;
    return anns.findIndex((a) => a.kind === "mask" && a.classId === cls);
  }, [anns, sel, cls]);
  const grid = useMemo(() => {
    const maxSide = 1024;
    const k = nat.w && nat.h ? Math.min(1, maxSide / Math.max(nat.w, nat.h)) : 1;
    return { w: Math.max(1, Math.round(nat.w * k)), h: Math.max(1, Math.round(nat.h * k)) };
  }, [nat]);
  const startPaint = () => {
    const c = paint.current;
    if (!c) return;
    c.width = grid.w; c.height = grid.h;
    const ctx = c.getContext("2d")!;
    ctx.clearRect(0, 0, grid.w, grid.h);
    const m = maskIndex >= 0 ? anns[maskIndex] : null;
    if (m) {
      const { bits, w, h } = decodeRle(m.geometry.rle as { size: number[]; counts: number[] });
      const img = ctx.createImageData(w, h);
      for (let i = 0; i < bits.length; i++) if (bits[i]) { img.data[i * 4 + 3] = 255; }
      ctx.putImageData(img, 0, 0);
    }
    painting.current = true;
  };
  const stroke = (p: { x: number; y: number }) => {
    const ctx = paint.current?.getContext("2d");
    if (!ctx) return;
    ctx.globalCompositeOperation = tool === "erase" ? "destination-out" : "source-over";
    ctx.fillStyle = "#000";
    ctx.beginPath();
    ctx.arc(p.x * grid.w, p.y * grid.h, (brushSize / 2 / view.s) * (grid.w / Math.max(1, nat.w)), 0, Math.PI * 2);
    ctx.fill();
  };
  const endPaint = () => {
    if (!painting.current) return;
    painting.current = false;
    const ctx = paint.current!.getContext("2d")!;
    const data = ctx.getImageData(0, 0, grid.w, grid.h).data;
    const bits = new Uint8Array(grid.w * grid.h);
    let any = false;
    for (let i = 0; i < bits.length; i++) if (data[i * 4 + 3] > 127) { bits[i] = 1; any = true; }
    const geometry = { rle: encodeRle(bits, grid.w, grid.h) };
    set((as) => {
      if (maskIndex >= 0) return any ? as.map((a, i) => (i === maskIndex ? { ...a, geometry, source: "human" } : a)) : as.filter((_, i) => i !== maskIndex);
      return any ? [...as, { kind: "mask", classId: cls, geometry, source: "human" }] : as;
    });
    ctx.clearRect(0, 0, grid.w, grid.h);
  };

  const onDown = (e: React.MouseEvent) => {
    if (e.button === 1 || space || tool === "pan") { setPanning({ x: e.clientX, y: e.clientY, vx: view.x, vy: view.y }); return; }
    if (readOnly || e.button !== 0 || !nat.w) return;
    const p = toImg(e);
    if (tool === "box") { setSel(null); setDrag({ x0: p.x, y0: p.y, x1: p.x, y1: p.y }); }
    else if (tool === "polygon") {
      if (poly.length >= 3 && Math.hypot((poly[0][0] - p.x) * nat.w, (poly[0][1] - p.y) * nat.h) * view.s < 10) finishPoly();
      else setPoly((ps) => [...ps, [p.x, p.y]]);
    } else if (tool === "keypoints") setKps((ks) => [...ks, [p.x, p.y, 2]]);
    else if (tool === "brush" || tool === "erase") { startPaint(); stroke(p); }
    else if (tool === "select") setSel(null);
  };
  const onMove = (e: React.MouseEvent) => {
    if (panning) { setView((v) => ({ ...v, x: panning.vx + e.clientX - panning.x, y: panning.vy + e.clientY - panning.y })); return; }
    if (!nat.w) return;
    const p = toImg(e);
    if (drag) setDrag({ ...drag, x1: p.x, y1: p.y });
    if (painting.current) stroke(p);
    if (move) {
      const dx = p.x - move.px, dy = p.y - move.py;
      set((as) => as.map((a, i) => {
        if (i !== move.i) return a;
        const g = move.orig.geometry as Record<string, number> & { points?: number[][] };
        if (a.kind === "bbox") {
          if (!move.handle) return { ...a, geometry: { ...g, x: r4(Math.min(1 - g.w, Math.max(0, g.x + dx))), y: r4(Math.min(1 - g.h, Math.max(0, g.y + dy))) } };
          let { x, y, w, h } = g;
          if (move.handle.includes("l")) { x = Math.min(g.x + g.w - 0.002, Math.max(0, g.x + dx)); w = g.x + g.w - x; }
          if (move.handle.includes("r")) w = Math.max(0.002, Math.min(1 - g.x, g.w + dx));
          if (move.handle.includes("t")) { y = Math.min(g.y + g.h - 0.002, Math.max(0, g.y + dy)); h = g.y + g.h - y; }
          if (move.handle.includes("b")) h = Math.max(0.002, Math.min(1 - g.y, g.h + dy));
          return { ...a, geometry: { ...g, x: r4(x), y: r4(y), w: r4(w), h: r4(h) } };
        }
        if (g.points) return { ...a, geometry: { ...g, points: g.points.map((pt) => [r4(pt[0] + dx), r4(pt[1] + dy), ...pt.slice(2)]) } };
        return a;
      }));
    }
  };
  const onUp = () => {
    if (panning) { setPanning(null); return; }
    if (drag) {
      const x = r4(Math.min(drag.x0, drag.x1)), y = r4(Math.min(drag.y0, drag.y1));
      const w = r4(Math.abs(drag.x1 - drag.x0)), h = r4(Math.abs(drag.y1 - drag.y0));
      if (w > 0.003 && h > 0.003) { set((as) => [...as, { kind: "bbox", classId: cls, geometry: { x, y, w: Math.min(w, 1 - x), h: Math.min(h, 1 - y) }, source: "human" }]); }
      setDrag(null);
    }
    if (painting.current) endPaint();
    setMove(null);
  };
  const grab = (i: number, handle?: string) => (e: React.MouseEvent) => {
    if (tool !== "select" || readOnly || e.button !== 0) return;
    e.stopPropagation();
    setSel(i);
    const p = toImg(e);
    setMove({ i, px: p.x, py: p.y, orig: anns[i], handle });
  };

  // Rendered mask layers (decoded once per change).
  const maskImages = useMemo(() => anns.map((a) => {
    if (a.kind !== "mask" || a.hidden) return null;
    try {
      const { bits, w, h } = decodeRle(a.geometry.rle as { size: number[]; counts: number[] });
      const c = document.createElement("canvas"); c.width = w; c.height = h;
      const ctx = c.getContext("2d")!; const img = ctx.createImageData(w, h);
      const hex = colorOf(classes, a.classId).replace("#", "");
      const [r, g, b] = [0, 2, 4].map((o) => parseInt(hex.slice(o, o + 2), 16) || 100);
      for (let i = 0; i < bits.length; i++) if (bits[i]) { img.data.set([r, g, b, 120], i * 4); }
      ctx.putImageData(img, 0, 0);
      return c.toDataURL();
    } catch { return null; }
  }), [anns, classes]);

  const W = nat.w || 1, H = nat.h || 1;
  const sw = 2 / view.s;   // constant on-screen stroke width
  const cursor = panning || space || tool === "pan" ? "grab" : tool === "select" ? "default" : "crosshair";
  return (
    <div className="flex h-full flex-col">
      <div className="flex flex-wrap items-center gap-2 border-b border-line bg-surface px-3 py-2">
        {!readOnly && <div className="flex rounded-lg border border-line p-0.5">
          {TOOLS.map(([k, label, Icon, key]) => (
            <Tip key={k} text={`${t(label)} (${key})`}>
              <button onClick={() => setTool(k)} aria-label={t(label)} aria-pressed={tool === k}
                className={`relative inline-flex h-10 min-w-10 items-center justify-center gap-1.5 rounded-md px-2 text-xs ${tool === k ? "bg-primary text-primary-fg" : "text-fg-muted hover:bg-surface-2 hover:text-fg"}`}>
                <Icon className="h-4 w-4" /><span className="hidden 2xl:inline">{t(label)}</span>
                <span className={`absolute bottom-0.5 right-1 text-[9px] leading-none ${tool === k ? "text-primary-fg/80" : "text-fg-subtle"}`}>{key}</span></button>
            </Tip>))}
        </div>}
        {(tool === "brush" || tool === "erase") && !readOnly && (
          <label className="flex items-center gap-2 text-xs text-fg-muted">{t("Brush")}
            <input type="range" min={2} max={120} value={brushSize} onChange={(e) => setBrushSize(Number(e.target.value))} /><span className="w-8">{brushSize}px</span></label>)}
        {tool === "polygon" && poly.length > 0 && <span className="text-xs tabular-nums text-fg-muted">{poly.length}</span>}
        {tool === "keypoints" && kps.length > 0 && <span className="text-xs tabular-nums text-fg-muted">{kps.length}</span>}
        <div className="ml-auto flex items-center gap-2">
          <TagBar anns={anns} set={set} classes={classes} readOnly={readOnly} />
          <span className="w-12 text-right text-xs text-fg-subtle">{Math.round(view.s * 100)}%</span>
          <Tip text={t("Fit (F)")}><button className="btn-ghost h-8 w-8 p-0" onClick={fit}><Maximize className="h-4 w-4" /></button></Tip>
        </div>
      </div>
      <div ref={wrap} className="relative min-h-0 flex-1 overflow-hidden bg-bg"
        style={{ cursor }} onWheel={onWheel} onMouseDown={onDown}
        onMouseMove={(e) => { onMove(e); if (tool === "box" || tool === "keypoints") { const r = wrap.current!.getBoundingClientRect(); setCross([e.clientX - r.left, e.clientY - r.top]); } }}
        onMouseUp={onUp} onMouseLeave={() => { onUp(); setCross(null); }}
        onDoubleClick={() => { if (tool === "polygon") finishPoly(); if (tool === "keypoints") finishKps(); }} onContextMenu={(e) => e.preventDefault()}>
        {/* crosshair guides for precise box edges */}
        {cross && !readOnly && (tool === "box" || tool === "keypoints") && <>
          <div className="pointer-events-none absolute inset-y-0 z-10 w-px bg-white/60 mix-blend-difference" style={{ left: cross[0] }} />
          <div className="pointer-events-none absolute inset-x-0 z-10 h-px bg-white/60 mix-blend-difference" style={{ top: cross[1] }} />
        </>}
        <div ref={stage} className="absolute left-0 top-0 origin-top-left select-none" style={{ width: W, height: H, transform: `translate(${view.x}px, ${view.y}px) scale(${view.s})` }}>
          <img src={item.url} alt={item.path} draggable={false} className="block h-full w-full" style={{ imageRendering: view.s > 3 ? "pixelated" : "auto" }}
            onLoad={(e) => setNat({ w: e.currentTarget.naturalWidth, h: e.currentTarget.naturalHeight })} />
          {maskImages.map((src, i) => src && <img key={i} src={src} alt="" draggable={false} className="pointer-events-none absolute inset-0 h-full w-full" style={{ imageRendering: "pixelated" }} />)}
          <canvas ref={paint} className="pointer-events-none absolute inset-0 h-full w-full opacity-50" />
          <svg className="absolute inset-0 h-full w-full overflow-visible" viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none">
            {anns.map((a, i) => {
              if (a.hidden) return null;
              if (layer === "human" && a.source === "model") return null;
              if (layer === "ai" && a.source !== "model") return null;
              const col = colorOf(classes, a.classId);
              const dash = a.source === "model" ? `${6 / view.s} ${4 / view.s}` : undefined;
              const g = a.geometry as Record<string, number> & { points?: number[][] };
              const active = sel === i;
              if (a.kind === "bbox") return (
                <g key={i} onMouseDown={grab(i)} style={{ cursor: tool === "select" ? "move" : undefined }}>
                  <rect x={g.x * W} y={g.y * H} width={g.w * W} height={g.h * H} fill={`${col}${active ? "33" : "14"}`} stroke={col} strokeWidth={active ? sw * 2 : sw * 1.4} strokeDasharray={dash} />
                  {/* class name on the box: a filled tag in the class color */}
                  {(() => {
                    const base = classes.find((c) => c.id === a.classId)?.name ?? String(a.classId);
                    const m = match?.get(i);
                    const iou = m ? (m.iou >= 0.05 ? ` · IoU ${m.iou.toFixed(2)}${m.same ? "" : " ≠"}` : " · ∅") : "";
                    const name = (a.source === "model" && a.confidence !== undefined ? `${base} ${a.confidence.toFixed(2)}` : base) + iou;
                    const fs = 12 / view.s, pad = 3 / view.s, tw = name.length * fs * 0.62 + pad * 2, th = fs + pad * 2;
                    const ty = g.y * H - th >= 0 ? g.y * H - th : g.y * H;
                    return (<g pointerEvents="none">
                      <rect x={g.x * W} y={ty} width={tw} height={th} fill={col} />
                      <text x={g.x * W + pad} y={ty + th - pad - fs * 0.18} fontSize={fs} fontWeight={600} fill="#fff" style={{ fontFamily: "Inter, sans-serif" }}>{name}</text>
                    </g>);
                  })()}
                  {active && tool === "select" && !readOnly && [["tl", g.x, g.y], ["tr", g.x + g.w, g.y], ["bl", g.x, g.y + g.h], ["br", g.x + g.w, g.y + g.h]].map(([hd, hx, hy]) => (
                    <rect key={hd as string} x={(hx as number) * W - 4 / view.s} y={(hy as number) * H - 4 / view.s} width={8 / view.s} height={8 / view.s}
                      fill="white" stroke={col} strokeWidth={sw / 2} style={{ cursor: `${hd === "tl" || hd === "br" ? "nwse" : "nesw"}-resize` }} onMouseDown={grab(i, hd as string)} />))}
                </g>);
              if (a.kind === "polygon" && g.points) return (
                <polygon key={i} points={g.points.map((p) => `${p[0] * W},${p[1] * H}`).join(" ")} onMouseDown={grab(i)}
                  fill={`${col}${active ? "44" : "26"}`} stroke={col} strokeWidth={active ? sw * 1.5 : sw} strokeDasharray={dash} style={{ cursor: tool === "select" ? "move" : undefined }} />);
              if (a.kind === "keypoints" && g.points) return (
                <g key={i} onMouseDown={grab(i)}>
                  <polyline points={g.points.map((p) => `${p[0] * W},${p[1] * H}`).join(" ")} fill="none" stroke={col} strokeWidth={sw / 1.5} strokeDasharray={`${3 / view.s} ${3 / view.s}`} />
                  {g.points.map((p, j) => <g key={j}><circle cx={p[0] * W} cy={p[1] * H} r={5 / view.s} fill={col} stroke="white" strokeWidth={sw / 2} />
                    <text x={p[0] * W + 7 / view.s} y={p[1] * H - 7 / view.s} fontSize={11 / view.s} fill="white" stroke="black" strokeWidth={0.6 / view.s}>{j + 1}</text></g>)}
                </g>);
              return null;
            })}
            {drag && <rect x={Math.min(drag.x0, drag.x1) * W} y={Math.min(drag.y0, drag.y1) * H} width={Math.abs(drag.x1 - drag.x0) * W} height={Math.abs(drag.y1 - drag.y0) * H}
              fill="transparent" stroke={colorOf(classes, cls)} strokeWidth={sw} strokeDasharray={`${4 / view.s} ${3 / view.s}`} />}
            {poly.length > 0 && <>
              <polyline points={poly.map(([x, y]) => `${x * W},${y * H}`).join(" ")} fill="none" stroke={colorOf(classes, cls)} strokeWidth={sw} />
              {poly.map(([x, y], j) => <circle key={j} cx={x * W} cy={y * H} r={(j === 0 ? 6 : 4) / view.s} fill={j === 0 ? "white" : colorOf(classes, cls)} stroke={colorOf(classes, cls)} strokeWidth={sw / 2} />)}
            </>}
            {kps.map(([x, y], j) => <circle key={j} cx={x * W} cy={y * H} r={5 / view.s} fill={colorOf(classes, cls)} stroke="white" strokeWidth={sw / 2} />)}
          </svg>
        </div>
      </div>
    </div>
  );
}

/* ═════════════════════════════ Text ═════════════════════════════ */

function useTextContent(url: string) {
  const [s, setS] = useState<{ text?: string; error?: string }>({});
  useEffect(() => {
    let live = true;
    setS({});
    fetch(url).then(async (r) => { if (!r.ok) throw new Error(`HTTP ${r.status}`); const x = await r.text(); if (live) setS({ text: x.length > 200_000 ? x.slice(0, 200_000) : x }); })
      .catch((e: Error) => { if (live) setS({ error: e.message }); });
    return () => { live = false; };
  }, [url]);
  return s;
}

function TextEditor({ item, anns, set, sel, setSel, cls, classes, readOnly }: EditorProps) {
  const { text, error } = useTextContent(item.url);
  const box = useRef<HTMLDivElement>(null);
  const spans = anns.map((a, i) => ({ a, i })).filter(({ a }) => a.kind === "span" && !a.hidden)
    .map(({ a, i }) => ({ i, start: Number(a.geometry.start), end: Number(a.geometry.end), classId: a.classId })).sort((x, y) => x.start - y.start);

  const onMouseUp = () => {
    if (readOnly || !text) return;
    const s = window.getSelection();
    if (!s || s.isCollapsed || !box.current?.contains(s.anchorNode)) return;
    const offset = (node: Node, off: number) => {
      const walker = document.createTreeWalker(box.current!, NodeFilter.SHOW_TEXT);
      let n = 0, cur: Node | null;
      while ((cur = walker.nextNode())) { if (cur === node) return n + off; n += cur.textContent?.length ?? 0; }
      return n;
    };
    let a = offset(s.anchorNode!, s.anchorOffset), b = offset(s.focusNode!, s.focusOffset);
    if (a > b) [a, b] = [b, a];
    if (b > a) { set((as) => [...as, { kind: "span", classId: cls, geometry: { start: a, end: b, text: text.slice(a, b) }, source: "human" }]); }
    s.removeAllRanges();
  };

  // Build the highlighted text (spans may not overlap visually; later spans that overlap are drawn after the earlier one).
  const parts: ReactNode[] = [];
  if (text !== undefined) {
    let pos = 0;
    for (const sp of spans) {
      if (sp.start < pos) continue;
      if (sp.start > pos) parts.push(text.slice(pos, sp.start));
      const col = colorOf(classes, sp.classId);
      parts.push(<mark key={sp.i} onClick={() => setSel(sp.i)} className="rounded px-0.5" style={{ background: `${col}44`, outline: sel === sp.i ? `2px solid ${col}` : undefined, color: "inherit" }}>
        {text.slice(sp.start, sp.end)}<sup className="ml-0.5 text-[10px] font-semibold" style={{ color: col }}>{classes.find((c) => c.id === sp.classId)?.name}</sup></mark>);
      pos = sp.end;
    }
    parts.push(text.slice(pos));
  }
  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center gap-3 border-b border-line bg-surface px-3 py-2">
        <TagBar anns={anns} set={set} classes={classes} readOnly={readOnly} />
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto p-6">
        {error && <ErrorBox error={t("Could not load the text: {error}", { error })} />}
        {text === undefined && !error && <div className="skeleton h-40" />}
        {text !== undefined && <div ref={box} onMouseUp={onMouseUp} className="mx-auto max-w-4xl whitespace-pre-wrap font-[450] leading-8 text-fg">{parts}</div>}
      </div>
    </div>
  );
}

/* ═════════════════════════════ Audio ═════════════════════════════ */

function useWaveform(url: string, bins = 1200) {
  const [peaks, setPeaks] = useState<number[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let live = true;
    setPeaks(null); setError(null);
    (async () => {
      try {
        const buf = await (await fetch(url)).arrayBuffer();
        const ctx = new AudioContext();
        const audio = await ctx.decodeAudioData(buf);
        void ctx.close();
        const ch = audio.getChannelData(0);
        const step = Math.max(1, Math.floor(ch.length / bins));
        const out: number[] = [];
        for (let i = 0; i < ch.length; i += step) { let m = 0; for (let j = i; j < Math.min(i + step, ch.length); j++) m = Math.max(m, Math.abs(ch[j])); out.push(m); }
        if (live) setPeaks(out);
      } catch (e) { if (live) setError((e as Error).message); }
    })();
    return () => { live = false; };
  }, [url, bins]);
  return { peaks, error };
}

function AudioEditor({ item, anns, set, sel, setSel, cls, classes, readOnly }: EditorProps) {
  const audio = useRef<HTMLAudioElement>(null);
  const bar = useRef<HTMLDivElement>(null);
  const { peaks, error } = useWaveform(item.url);
  const [dur, setDur] = useState(0);
  const [cur, setCur] = useState(0);
  const [drag, setDrag] = useState<{ a: number; b: number } | null>(null);
  const transcriptIdx = anns.findIndex((a) => a.kind === "transcript");
  const transcript = transcriptIdx >= 0 ? String(anns[transcriptIdx].geometry.text ?? "") : "";
  const setTranscript = (v: string) => set((as) => (transcriptIdx >= 0 ? as.map((a, i) => (i === transcriptIdx ? { ...a, geometry: { text: v } } : a))
    : [...as, { kind: "transcript", classId: 0, geometry: { text: v }, source: "human" }]));
  const tAt = (e: React.MouseEvent) => { const r = bar.current!.getBoundingClientRect(); return Math.max(0, Math.min(dur, ((e.clientX - r.left) / r.width) * dur)); };
  const fmtT = (s: number) => `${Math.floor(s / 60)}:${(s % 60).toFixed(1).padStart(4, "0")}`;
  return (
    <div className="flex h-full flex-col gap-4 overflow-y-auto p-6">
      <div className="flex items-center gap-3"><TagBar anns={anns} set={set} classes={classes} readOnly={readOnly} /></div>
      <audio ref={audio} src={item.url} controls className="w-full" onLoadedMetadata={(e) => setDur(e.currentTarget.duration)} onTimeUpdate={(e) => setCur(e.currentTarget.currentTime)} />
      {error && <ErrorBox error={t("Could not draw the waveform: {error}", { error })} />}
      <div ref={bar} className="relative h-36 cursor-crosshair select-none rounded-lg border border-line bg-surface"
        onMouseDown={(e) => { if (!readOnly && dur) { const x = tAt(e); setDrag({ a: x, b: x }); } }}
        onMouseMove={(e) => { if (drag) setDrag({ ...drag, b: tAt(e) }); }}
        onMouseUp={() => {
          if (!drag) return;
          const [s, en] = [Math.min(drag.a, drag.b), Math.max(drag.a, drag.b)];
          if (en - s > 0.05) { set((as) => [...as, { kind: "segment", classId: cls, geometry: { start: Math.round(s * 1000) / 1000, end: Math.round(en * 1000) / 1000 }, source: "human" }]); }
          else if (audio.current) audio.current.currentTime = s;
          setDrag(null);
        }}>
        {peaks && <svg className="absolute inset-0 h-full w-full" viewBox={`0 0 ${peaks.length} 100`} preserveAspectRatio="none">
          {peaks.map((p, i) => <line key={i} x1={i} x2={i} y1={50 - p * 48} y2={50 + p * 48} stroke="var(--t-fg-subtle)" strokeWidth={1} />)}</svg>}
        {!peaks && !error && <div className="skeleton absolute inset-2" />}
        {dur > 0 && anns.map((a, i) => a.kind === "segment" && !a.hidden && (
          <div key={i} onMouseDown={(e) => { e.stopPropagation(); setSel(i); if (audio.current) audio.current.currentTime = Number(a.geometry.start); }}
            className="absolute inset-y-0 border-x-2" style={{ left: `${(Number(a.geometry.start) / dur) * 100}%`, width: `${((Number(a.geometry.end) - Number(a.geometry.start)) / dur) * 100}%`,
              background: `${colorOf(classes, a.classId)}${sel === i ? "55" : "33"}`, borderColor: colorOf(classes, a.classId) }}>
            <span className="absolute left-1 top-1 rounded px-1 text-[10px] text-white" style={{ background: colorOf(classes, a.classId) }}>{classes.find((c) => c.id === a.classId)?.name}</span>
          </div>))}
        {drag && dur > 0 && <div className="absolute inset-y-0 border-x border-dashed" style={{ left: `${(Math.min(drag.a, drag.b) / dur) * 100}%`, width: `${(Math.abs(drag.b - drag.a) / dur) * 100}%`, background: `${colorOf(classes, cls)}33`, borderColor: colorOf(classes, cls) }} />}
        {dur > 0 && <div className="pointer-events-none absolute inset-y-0 w-px bg-primary" style={{ left: `${(cur / dur) * 100}%` }} />}
      </div>
      <div className="text-xs text-fg-subtle">{fmtT(cur)} / {fmtT(dur)}</div>
      <label className="block">
        <span className="label">{t("Transcript")}</span>
        <textarea className="input min-h-32 leading-7" disabled={readOnly} value={transcript} onChange={(e) => setTranscript(e.target.value)} placeholder={t("Type what is said…")} />
      </label>
    </div>
  );
}

/* ═════════════════════════════ Video ═════════════════════════════ */

type VBox = { x: number; y: number; w: number; h: number; t: number; track: number };
/** Box of a track at time t: the keyframe at t, else linear interpolation between the surrounding keyframes. */
function boxAt(keys: VBox[], tm: number): (VBox & { interpolated: boolean }) | null {
  const ks = [...keys].sort((a, b) => a.t - b.t);
  if (ks.length === 0 || tm < ks[0].t - 0.02 || tm > ks[ks.length - 1].t + 0.02) return null;
  const exact = ks.find((k) => Math.abs(k.t - tm) < 0.02);
  if (exact) return { ...exact, interpolated: false };
  const i = ks.findIndex((k) => k.t > tm);
  const a = ks[i - 1], b = ks[i];
  if (!a || !b) return null;
  const f = (tm - a.t) / (b.t - a.t);
  const l = (p: number, q: number) => p + (q - p) * f;
  return { x: l(a.x, b.x), y: l(a.y, b.y), w: l(a.w, b.w), h: l(a.h, b.h), t: tm, track: a.track, interpolated: true };
}

function VideoEditor({ item, anns, set, setSel, cls, classes, readOnly }: EditorProps) {
  const video = useRef<HTMLVideoElement>(null);
  const stage = useRef<HTMLDivElement>(null);
  const [tm, setTm] = useState(0);
  const [fps] = useSticky("label:fps", 25);
  const [drag, setDrag] = useState<{ x0: number; y0: number; x1: number; y1: number } | null>(null);
  const [track, setTrack] = useState<number | null>(null);
  const tracks = useMemo(() => {
    const m = new Map<number, { classId: number; keys: { box: VBox; idx: number }[] }>();
    anns.forEach((a, idx) => {
      if (a.kind !== "bbox" || a.hidden || a.geometry.track === undefined) return;
      const b = a.geometry as unknown as VBox;
      const e = m.get(b.track) ?? { classId: a.classId, keys: [] };
      e.keys.push({ box: b, idx }); m.set(b.track, e);
    });
    return m;
  }, [anns]);
  const pos = (e: React.MouseEvent) => { const r = stage.current!.getBoundingClientRect(); return { x: (e.clientX - r.left) / r.width, y: (e.clientY - r.top) / r.height }; };
  const step = (n: number) => { const v = video.current; if (v) { v.pause(); v.currentTime = Math.max(0, v.currentTime + n / fps); } };
  const onUp = () => {
    if (!drag) return;
    const x = r4(Math.min(drag.x0, drag.x1)), y = r4(Math.min(drag.y0, drag.y1)), w = r4(Math.abs(drag.x1 - drag.x0)), h = r4(Math.abs(drag.y1 - drag.y0));
    setDrag(null);
    if (w < 0.003 || h < 0.003) return;
    const id = track ?? (Math.max(-1, ...tracks.keys()) + 1);
    const tr = Math.round(tm * 1000) / 1000;
    set((as) => [...as.filter((a) => !(a.kind === "bbox" && a.geometry.track === id && Math.abs(Number(a.geometry.t) - tr) < 0.02)),
      { kind: "bbox", classId: tracks.get(id)?.classId ?? cls, geometry: { x, y, w: Math.min(w, 1 - x), h: Math.min(h, 1 - y), t: tr, track: id }, source: "human" }]);
    setTrack(id);
  };
  return (
    <div className="flex h-full flex-col">
      <div className="flex flex-wrap items-center gap-2 border-b border-line bg-surface px-3 py-2 text-xs">
        <button className="btn-ghost h-8" onClick={() => step(-1)}>{t("← Frame")}</button>
        <button className="btn-ghost h-8" onClick={() => step(1)}>{t("Frame →")}</button>
        <span className="mono text-fg-muted">{tm.toFixed(2)}s · {t("frame {n}", { n: Math.round(tm * fps) })}</span>
        <select className="input h-8 w-auto" value={track ?? ""} onChange={(e) => setTrack(e.target.value === "" ? null : Number(e.target.value))}>
          <option value="">{t("New track")}</option>
          {[...tracks.entries()].map(([id, tr]) => <option key={id} value={id}>{t("Track {n}", { n: id + 1 })} · {classes.find((c) => c.id === tr.classId)?.name}</option>)}
        </select>
      </div>
      <div className="flex min-h-0 flex-1 items-center justify-center bg-black/60 p-4">
        <div ref={stage} className="relative inline-block select-none" style={{ cursor: readOnly ? "default" : "crosshair" }}
          onMouseDown={(e) => { if (readOnly || e.button !== 0) return; video.current?.pause(); const p = pos(e); setDrag({ x0: p.x, y0: p.y, x1: p.x, y1: p.y }); }}
          onMouseMove={(e) => { if (drag) { const p = pos(e); setDrag({ ...drag, x1: p.x, y1: p.y }); } }} onMouseUp={onUp} onMouseLeave={onUp}>
          <video ref={video} src={item.url} controls className="block max-h-[70vh] max-w-full" onTimeUpdate={(e) => setTm(e.currentTarget.currentTime)} onSeeked={(e) => setTm(e.currentTarget.currentTime)} />
          {[...tracks.entries()].map(([id, tr]) => {
            const b = boxAt(tr.keys.map((k) => k.box), tm);
            if (!b) return null;
            const col = colorOf(classes, tr.classId);
            return (
              <div key={id} className="absolute border-2" onMouseDown={(e) => { e.stopPropagation(); setTrack(id); const k = tr.keys.find((x) => Math.abs(x.box.t - tm) < 0.02); if (k) setSel(k.idx); }}
                style={{ left: `${b.x * 100}%`, top: `${b.y * 100}%`, width: `${b.w * 100}%`, height: `${b.h * 100}%`, borderColor: col, borderStyle: b.interpolated ? "dashed" : "solid",
                  boxShadow: track === id ? "0 0 0 2px white" : undefined }}>
                <span className="absolute -top-5 left-0 whitespace-nowrap px-1 text-[11px] text-white" style={{ background: col }}>
                  {t("Track {n}", { n: id + 1 })} · {classes.find((c) => c.id === tr.classId)?.name}</span>
              </div>);
          })}
          {drag && <div className="absolute border-2 border-dashed" style={{ borderColor: colorOf(classes, cls), left: `${Math.min(drag.x0, drag.x1) * 100}%`, top: `${Math.min(drag.y0, drag.y1) * 100}%`,
            width: `${Math.abs(drag.x1 - drag.x0) * 100}%`, height: `${Math.abs(drag.y1 - drag.y0) * 100}%` }} />}
        </div>
      </div>
      <div className="border-t border-line bg-surface px-3 py-2"><TagBar anns={anns} set={set} classes={classes} readOnly={readOnly} /></div>
    </div>
  );
}
