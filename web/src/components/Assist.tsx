import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle, BrainCircuit, CheckCircle2, CircleX, Globe, Loader2, Play, Plus, Rocket, Square, Trash2, Wand2, X,
} from "lucide-react";
import { api, errorText } from "../lib/api";
import type { AssistSource, SuggestionSet, ProposedLabel } from "../gen/mlplatform/v1/labeling_pb";
import { Dialog, EmptyState, ErrorBox, Field, RelTime, Skeleton } from "./ui";
import { RunSheet } from "./AiActivity";
import { useSticky } from "../lib/persist";
import { translate as t } from "../i18n";

/*
 * AI label proposals (Hermes phase 3). Sources, mapping and the external-API switch are visible and explicit; every proposal
 * is a stored suggestion set (pending until a person applies it) with the AI call behind it one click away.
 */

type Cls = { id: number; name: string; color?: string };
const parseClasses = (json: string): Cls[] => { try { return JSON.parse(json) as Cls[]; } catch { return []; } };
const STATE_CLS: Record<string, string> = { ready: "text-success", degraded: "text-warning", unavailable: "text-danger" };
const JOB_STATE: Record<string, string> = { queued: "queued", running: "running", succeeded: "done", failed: "failed", cancelled: "stopped" };
const MEDIA_LABEL: Record<string, string> = { image: "image", text: "text", audio: "audio", video: "video" };

export function ExternalBadge() {
  return <span className="badge bg-warning/15 text-warning"><Globe className="h-3 w-3" />{t("external API")}</span>;
}

function SourceLine({ s }: { s: AssistSource }) {
  return (
    <span className="min-w-0">
      <span className="flex flex-wrap items-center gap-1.5">
        {s.kind === "model" ? <BrainCircuit className="h-3.5 w-3.5 text-fg-subtle" /> : <Rocket className="h-3.5 w-3.5 text-fg-subtle" />}
        <span className="font-medium">{s.label}</span>
        {s.external && <ExternalBadge />}
        {s.media.map((m) => <span key={m} className="badge bg-surface-2 text-fg-muted">{t(MEDIA_LABEL[m] ?? m)}</span>)}
      </span>
      <span className="block text-xs text-fg-subtle">{t(s.detail)} · <span className={STATE_CLS[s.state] ?? ""}>{t(s.stateReason)}</span></span>
    </span>
  );
}

/* ───────── Project tab ───────── */

export function AssistProjectTab({ ws, projectId }: { ws: string; projectId: string }) {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ["assist", ws, projectId], queryFn: () => api.labeling.getAssistSettings({ workspaceId: ws, projectId }) });
  const s = q.data;
  const classes = useMemo(() => parseClasses(s?.classesJson ?? "[]"), [s?.classesJson]);
  const [form, setForm] = useState<{ enabled: Set<string>; mapping: [string, number][]; allowExternal: boolean; minConfidence: number } | null>(null);
  useEffect(() => {
    if (!s) return;
    setForm({
      enabled: new Set(s.sources.filter((x) => x.enabled).map((x) => `${x.kind}:${x.id}`)),
      mapping: Object.entries(s.classMapping), allowExternal: s.allowExternal, minConfidence: s.minConfidence,
    });
  }, [s]);
  const save = useMutation({
    mutationFn: () => api.labeling.saveAssistSettings({
      workspaceId: ws, projectId, allowExternal: form!.allowExternal, minConfidence: form!.minConfidence,
      sources: [...form!.enabled].map((k) => { const [kind, id] = k.split(":"); return { kind, id }; }),
      classMapping: Object.fromEntries(form!.mapping.filter(([k]) => k.trim())),
    }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["assist", ws, projectId] }),
  });
  if (q.isLoading || !form) return <Skeleton rows={5} />;
  if (!s) return <ErrorBox error={q.error ? errorText(q.error) : null} onRetry={() => void q.refetch()} />;
  const ro = !s.canManage;
  const enabledSources = s.sources.filter((x) => form.enabled.has(`${x.kind}:${x.id}`));
  return (
    <>
      <ErrorBox error={save.error ? errorText(save.error) : null} />
      <div className="grid gap-4 xl:grid-cols-2">
        <section className="card">
          <h2 className="mb-1 text-sm font-semibold">{t("Proposal sources")}</h2>
          {s.sources.length === 0 && <p className="text-sm text-fg-muted">{t("No source yet: declare an AI model with the labeler or vision role (AI models page), or deploy a model (Deployments).")}</p>}
          <div className="space-y-1">
            {s.sources.map((x) => {
              const key = `${x.kind}:${x.id}`;
              return (
                <label key={key} className="flex items-start gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-surface-2">
                  <input type="checkbox" className="mt-1" disabled={ro} checked={form.enabled.has(key)}
                    onChange={(e) => { const n = new Set(form.enabled); if (e.target.checked) n.add(key); else n.delete(key); setForm({ ...form, enabled: n }); }} />
                  <SourceLine s={x} />
                </label>);
            })}
          </div>
          <div className="mt-4 space-y-3 border-t border-line pt-3">
            <label className="flex items-start gap-2 text-sm">
              <input type="checkbox" className="mt-1" disabled={ro} checked={form.allowExternal} onChange={(e) => setForm({ ...form, allowExternal: e.target.checked })} />
              <span><span className="flex items-center gap-1.5">{t("Allow external APIs")}<ExternalBadge /></span>
                <span className="block text-xs text-fg-muted">{t("Items are sent to a provider outside the platform. Even when allowed, each call asks for a separate confirmation.")}</span></span>
            </label>
            <Field label={t("Minimum confidence")}>
              <div className="flex items-center gap-3">
                <input type="range" min={0} max={0.95} step={0.05} className="flex-1" disabled={ro} value={form.minConfidence}
                  onChange={(e) => setForm({ ...form, minConfidence: Number(e.target.value) })} />
                <span className="w-12 text-right text-sm tabular-nums">{Math.round(form.minConfidence * 100)}%</span>
              </div>
            </Field>
          </div>
        </section>

        <section className="card">
          <h2 className="mb-1 text-sm font-semibold">{t("Class mapping")}</h2>
          <div className="space-y-2">
            {form.mapping.map(([k, v], i) => (
              <div key={i} className="flex items-center gap-2">
                <input className="input mono flex-1" disabled={ro} placeholder={t("model label")} value={k}
                  onChange={(e) => setForm({ ...form, mapping: form.mapping.map((m, j) => (j === i ? [e.target.value, m[1]] : m)) })} />
                <span className="text-fg-subtle">→</span>
                <select className="input w-44" disabled={ro} value={v} onChange={(e) => setForm({ ...form, mapping: form.mapping.map((m, j) => (j === i ? [m[0], Number(e.target.value)] : m)) })}>
                  {classes.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
                {!ro && <button className="text-fg-subtle hover:text-danger" onClick={() => setForm({ ...form, mapping: form.mapping.filter((_, j) => j !== i) })} aria-label={t("Delete")}><Trash2 className="h-4 w-4" /></button>}
              </div>))}
            {!ro && <button className="btn-ghost" disabled={classes.length === 0} onClick={() => setForm({ ...form, mapping: [...form.mapping, ["", classes[0]?.id ?? 0]] })}><Plus className="h-4 w-4" />{t("Add mapping")}</button>}
          </div>
          {!ro && <div className="mt-4 flex justify-end border-t border-line pt-3">
            <button className="btn-primary" disabled={save.isPending} onClick={() => save.mutate()}>{save.isPending ? t("Saving…") : t("Save")}</button></div>}
          {ro && <p className="mt-3 text-xs text-fg-subtle">{t("Only a Lead can change these settings.")}</p>}
        </section>
      </div>
      <Prelabel ws={ws} projectId={projectId} sources={s.saved ? s.sources.filter((x) => x.enabled) : enabledSources} canManage={s.canManage} allowExternal={s.allowExternal} />
      <RecentProposals ws={ws} projectId={projectId} classes={classes} />
    </>
  );
}

function Prelabel({ ws, projectId, sources, canManage, allowExternal }: { ws: string; projectId: string; sources: AssistSource[]; canManage: boolean; allowExternal: boolean }) {
  const qc = useQueryClient();
  const jobs = useQuery({ queryKey: ["prelabel", ws, projectId], queryFn: () => api.labeling.listPrelabelJobs({ workspaceId: ws, projectId }),
    refetchInterval: (x) => (x.state.data?.jobs.some((j) => j.state === "queued" || j.state === "running") ? 2000 : 15000) });
  const [src, setSrc] = useState("");
  const [limit, setLimit] = useState(0);
  const [confirm, setConfirm] = useState(false);
  const chosen = sources.find((x) => `${x.kind}:${x.id}` === src);
  const start = useMutation({
    mutationFn: () => api.labeling.startPrelabel({ workspaceId: ws, projectId, source: { kind: chosen!.kind, id: chosen!.id }, limit, confirmExternal: confirm }),
    onSuccess: () => { setConfirm(false); void qc.invalidateQueries({ queryKey: ["prelabel", ws, projectId] }); },
  });
  const cancel = useMutation({ mutationFn: (jobId: string) => api.labeling.cancelPrelabel({ workspaceId: ws, projectId, jobId }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["prelabel", ws, projectId] }) });
  const targets = Number(jobs.data?.targets ?? 0n);
  return (
    <section className="card mt-4">
      <h2 className="mb-1 flex items-center gap-2 text-sm font-semibold"><Wand2 className="h-4 w-4 text-primary" />{t("Batch pre-label")}</h2>
      <ErrorBox error={start.error ? errorText(start.error) : cancel.error ? errorText(cancel.error) : null} />
      {canManage && (
        <div className="flex flex-wrap items-end gap-3">
          <Field label={t("Source")}>
            <select className="input w-80" value={src} onChange={(e) => { setSrc(e.target.value); setConfirm(false); }}>
              <option value="">{sources.length === 0 ? t("Enable a source above first") : t("— choose —")}</option>
              {sources.map((x) => <option key={`${x.kind}:${x.id}`} value={`${x.kind}:${x.id}`}>{x.label}{x.external ? ` · ${t("external API")}` : ""}</option>)}
            </select>
          </Field>
          <Field label={t("Items")}>
            <input className="input w-28" type="number" min={0} value={limit} onChange={(e) => setLimit(Math.max(0, Number(e.target.value)))} />
          </Field>
          {chosen?.external && (
            <label className="flex max-w-sm items-start gap-2 rounded-md border border-warning/30 bg-warning/10 p-2 text-xs text-warning">
              <input type="checkbox" className="mt-0.5" checked={confirm} disabled={!allowExternal} onChange={(e) => setConfirm(e.target.checked)} />
              {allowExternal ? t("I confirm that these items are sent to {name}, outside the platform.", { name: chosen.label }) : t("External APIs are off for this project.")}
            </label>)}
          <button className="btn-primary" disabled={!chosen || start.isPending || targets === 0 || (chosen.external && !confirm)} onClick={() => start.mutate()}>
            <Play className="h-4 w-4" />{t("Start")}</button>
        </div>)}
      <div className="mt-4 space-y-2">
        {(jobs.data?.jobs ?? []).map((j) => {
          const pct = j.total ? Math.round(((j.done + j.failed) / j.total) * 100) : 0;
          const live = j.state === "queued" || j.state === "running";
          return (
            <div key={j.id} className="rounded-lg border border-line p-2.5 text-sm">
              <div className="flex flex-wrap items-center gap-2">
                {live ? <Loader2 className="h-4 w-4 animate-spin text-info" /> : j.state === "succeeded" ? <CheckCircle2 className="h-4 w-4 text-success" /> : <CircleX className="h-4 w-4 text-danger" />}
                <span className="font-medium">{j.sourceLabel}</span>
                <span className="text-xs text-fg-muted">{t(JOB_STATE[j.state] ?? j.state)} · {t("{done}/{total} done, {failed} failed", { done: j.done, total: j.total, failed: j.failed })}</span>
                <span className="ml-auto text-xs text-fg-subtle">{j.createdByName} · <RelTime at={j.createdAt} /></span>
                {live && canManage && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => cancel.mutate(j.id)}><Square className="h-3 w-3" />{t("Stop")}</button>}
              </div>
              <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-surface-2"><div className="h-full bg-primary" style={{ width: `${pct}%` }} /></div>
              {j.error && <div className="mt-1 text-xs text-warning">{t(j.error)}</div>}
            </div>);
        })}
      </div>
    </section>
  );
}

function RecentProposals({ ws, projectId, classes }: { ws: string; projectId: string; classes: Cls[] }) {
  const q = useQuery({ queryKey: ["suggestions", ws, projectId], refetchInterval: 5000,
    queryFn: () => api.labeling.listSuggestionSets({ workspaceId: ws, projectId, path: "", jobId: "", limit: 100 }) });
  const [open, setOpen] = useState<SuggestionSet | null>(null);
  return (
    <section className="mt-4">
      <h2 className="mb-2 text-sm font-semibold">{t("Recent proposals")}</h2>
      {(q.data?.sets.length ?? 0) === 0 ? <EmptyState title={t("No proposal yet")} /> : (
        <div className="card divide-y divide-line p-0">
          {q.data!.sets.map((x) => (
            <button key={x.id} className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2.5 text-left text-sm hover:bg-surface-2" onClick={() => setOpen(x)}>
              <DecisionBadge s={x} />
              <span className="mono max-w-[30%] truncate">{x.path}</span>
              <span className="min-w-0 flex-1 truncate text-fg-muted">{x.sourceLabel} — {t(x.detail)}</span>
              {x.external && <ExternalBadge />}
              <span className="text-xs text-fg-subtle">{x.createdByName} · <RelTime at={x.createdAt} /></span>
            </button>))}
        </div>)}
      {open && <Dialog wide title={t("AI proposal")} description={<span className="mono">{open.path}</span>} onClose={() => setOpen(null)}><SetDetail s={open} classes={classes} /></Dialog>}
    </section>
  );
}

function DecisionBadge({ s }: { s: SuggestionSet }) {
  if (s.state === "failed") return <span className="badge bg-danger/15 text-danger"><CircleX className="h-3 w-3" />{t("failed")}</span>;
  const map: Record<string, [string, string]> = { pending: ["bg-info/15 text-info", "pending"], applied: ["bg-success/15 text-success", "applied"], dismissed: ["bg-surface-2 text-fg-muted", "dismissed"] };
  const [cls, label] = map[s.decision] ?? map.pending;
  return <span className={`badge ${cls}`}>{t(label)}</span>;
}

function SetDetail({ s, classes }: { s: SuggestionSet; classes?: Cls[] }) {
  const [run, setRun] = useState(false);
  return (
    <div className="space-y-2 text-sm">
      <div className="flex flex-wrap items-center gap-2"><DecisionBadge s={s} /><span className="font-medium">{s.sourceLabel}</span>{s.external && <ExternalBadge />}
        <span className="ml-auto text-xs text-fg-subtle">{s.latencyMs} ms · <RelTime at={s.createdAt} /></span></div>
      <div className={s.state === "failed" ? "text-danger" : "text-fg-muted"}>{t(s.detail)}</div>
      {s.items.length > 0 && (
        <ul className="space-y-0.5 text-xs">
          {s.items.map((it, i) => <li key={i} className="flex items-center gap-2">
            <span className="badge bg-surface-2 text-fg-muted">{it.kind}</span>
            <span className="font-medium">{classes?.find((c) => c.id === it.classId)?.name ?? it.label}</span>
            {it.label && classes?.find((c) => c.id === it.classId)?.name?.toLowerCase() !== it.label.toLowerCase() && <span className="text-fg-subtle">({t("model said")} “{it.label}”)</span>}
            {it.confidence >= 0 && <span className="ml-auto tabular-nums text-fg-muted">{Math.round(it.confidence * 100)}%</span>}
          </li>)}
        </ul>)}
      {s.unmapped.length > 0 && <div className="rounded-md border border-warning/30 bg-warning/10 px-2 py-1 text-xs text-warning">
        {t("Labels with no schema class (add a mapping to use them)")}: {s.unmapped.map((u) => `${u.label} ×${u.count}`).join(", ")}</div>}
      {s.aiRunId && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setRun(true)}><BrainCircuit className="h-3.5 w-3.5" />{t("Open the AI call")}</button>}
      {run && <RunSheet id={s.aiRunId} onClose={() => setRun(false)} />}
    </div>
  );
}

/* ───────── Editor panel ───────── */

export type ProposedAnn = { kind: string; classId: number; geometry: Record<string, unknown>; source: string; confidence?: number };

export function toAnns(items: ProposedLabel[]): ProposedAnn[] {
  return items.map((it) => ({
    kind: it.kind, classId: it.classId, source: "model", confidence: it.confidence >= 0 ? it.confidence : undefined,
    geometry: (() => { try { return JSON.parse(it.geometryJson) as Record<string, unknown>; } catch { return {}; } })(),
  }));
}

export function AssistPanel({ ws, projectId, path, media, classesJson, readOnly, onApply }: {
  ws: string; projectId: string; path: string; media: string; classesJson: string; readOnly: boolean; onApply: (anns: ProposedAnn[]) => void;
}) {
  const qc = useQueryClient();
  const [open, setOpen] = useSticky<boolean>("assist.panel.open", true);
  const settings = useQuery({ queryKey: ["assist", ws, projectId], queryFn: () => api.labeling.getAssistSettings({ workspaceId: ws, projectId }), staleTime: 30_000 });
  const key = ["suggestions-item", ws, projectId, path];
  const sets = useQuery({ queryKey: key, queryFn: () => api.labeling.listSuggestionSets({ workspaceId: ws, projectId, path, jobId: "", limit: 10 }) });
  const [external, setExternal] = useState<AssistSource | null>(null);
  const classes = useMemo(() => parseClasses(classesJson), [classesJson]);
  const propose = useMutation({
    mutationFn: (v: { s: AssistSource; confirm: boolean }) =>
      api.labeling.proposeItem({ workspaceId: ws, projectId, path, source: { kind: v.s.kind, id: v.s.id }, confirmExternal: v.confirm }),
    onSuccess: () => { setExternal(null); void qc.invalidateQueries({ queryKey: key }); },
  });
  const decide = useMutation({
    mutationFn: (v: { id: string; decision: string }) => api.labeling.decideSuggestionSet({ workspaceId: ws, projectId, id: v.id, decision: v.decision }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: key }),
  });
  const usable = (settings.data?.sources ?? []).filter((x) => x.enabled && x.media.includes(media));
  const internal = usable.filter((x) => !x.external);
  const ext = usable.filter((x) => x.external);
  const pending = (sets.data?.sets ?? []).filter((x) => x.decision === "pending");
  const past = (sets.data?.sets ?? []).filter((x) => x.decision !== "pending").slice(0, 3);
  return (
    <div>
      <button className="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wider text-fg-subtle" onClick={() => setOpen(!open)} aria-expanded={open}>
        <Wand2 className="h-3.5 w-3.5 text-primary" />{t("AI proposals")}{pending.length > 0 && <span className="rounded-full bg-info/20 px-1.5 text-info">{pending.length}</span>}</button>
      {open && (
        <div className="space-y-2">
          <ErrorBox error={propose.error ? errorText(propose.error) : decide.error ? errorText(decide.error) : null} />
          {!readOnly && (
            usable.length === 0 ? <p className="text-xs text-fg-subtle">—</p> : (
              <div className="flex flex-wrap gap-1.5">
                {internal.map((x) => (
                  <button key={x.id} className="btn-ghost h-7 px-2 text-xs" disabled={propose.isPending || x.state === "unavailable"} title={t(x.stateReason)}
                    onClick={() => propose.mutate({ s: x, confirm: false })}>
                    {propose.isPending && propose.variables?.s.id === x.id ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Wand2 className="h-3.5 w-3.5" />}{x.label}</button>))}
                {ext.map((x) => (
                  <button key={x.id} className="btn-ghost h-7 border border-warning/40 px-2 text-xs text-warning" disabled={propose.isPending || !settings.data?.allowExternal}
                    title={settings.data?.allowExternal ? t("Sends this item outside the platform") : t("External APIs are off for this project")} onClick={() => setExternal(x)}>
                    <Globe className="h-3.5 w-3.5" />{x.label}…</button>))}
              </div>))}
          {propose.isPending && <div className="flex items-center gap-2 text-xs text-fg-muted"><Loader2 className="h-3.5 w-3.5 animate-spin" />{t("Asking {name}…", { name: propose.variables?.s.label ?? "" })}</div>}
          {pending.map((x) => (
            <div key={x.id} className="rounded-lg border border-info/30 bg-info/5 p-2">
              <SetDetail s={x} classes={classes} />
              {!readOnly && x.state === "proposed" && (
                <div className="mt-2 flex gap-1.5">
                  <button className="btn-primary h-7 flex-1 px-2 text-xs" disabled={x.items.length === 0 || decide.isPending}
                    onClick={() => { onApply(toAnns(x.items)); decide.mutate({ id: x.id, decision: "applied" }); }}>
                    <CheckCircle2 className="h-3.5 w-3.5" />{t("Apply {n}", { n: x.items.length })}</button>
                  <button className="btn-ghost h-7 px-2 text-xs" disabled={decide.isPending} onClick={() => decide.mutate({ id: x.id, decision: "dismissed" })}><X className="h-3.5 w-3.5" />{t("Dismiss")}</button>
                </div>)}
              {!readOnly && x.state === "failed" && <button className="btn-ghost mt-2 h-7 px-2 text-xs" onClick={() => decide.mutate({ id: x.id, decision: "dismissed" })}><X className="h-3.5 w-3.5" />{t("Hide")}</button>}
            </div>))}
          {past.length > 0 && <div className="space-y-0.5 text-[11px] text-fg-subtle">
            {past.map((x) => <div key={x.id} className="flex items-center gap-1.5"><DecisionBadge s={x} /><span className="truncate">{x.sourceLabel}</span><RelTime at={x.decidedAt ?? x.createdAt} /></div>)}
          </div>}
        </div>)}
      {external && (
        <Dialog title={t("Send this item to an external API?")} onClose={() => setExternal(null)}
          description={t("The data leaves the platform. Use this only when the project allows it.")}>
          <div className="space-y-3 text-sm">
            <div className="flex items-start gap-2 rounded-lg border border-warning/30 bg-warning/10 p-3 text-warning"><AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{t("{name} receives the item content ({media}), the class names and the project guideline. The call is recorded in the AI log.", { name: external.label, media: t(MEDIA_LABEL[media] ?? media) })}</span></div>
            <div className="mono text-xs text-fg-muted">{path}</div>
            <ErrorBox error={propose.error ? errorText(propose.error) : null} />
            <div className="flex justify-end gap-2 border-t border-line pt-3">
              <button className="btn-ghost" onClick={() => setExternal(null)}>{t("Cancel")}</button>
              <button className="btn-primary" disabled={propose.isPending} onClick={() => propose.mutate({ s: external, confirm: true })}>
                {propose.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Globe className="h-4 w-4" />}{t("Send and propose")}</button>
            </div>
          </div>
        </Dialog>)}
    </div>
  );
}
