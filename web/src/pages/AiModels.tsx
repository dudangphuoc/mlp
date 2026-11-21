import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle, CheckCircle2, CircleDashed, CircleX, FlaskConical, KeyRound, Loader2, Pencil, Plug, Plus, Power, Search, ShieldCheck, Trash2,
} from "lucide-react";
import { api, errorText } from "../lib/api";
import type { AiCatalog, AiConnection, AiModel, AiOverview, AiRoleReadiness } from "../gen/mlplatform/v1/ai_pb";
import type { Me } from "../gen/mlplatform/v1/iam_pb";
import { ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, PageHeader, RelTime, Skeleton, Tabs } from "../components/ui";
import { fmtMs, ROLE_LABEL, RunRow, RunSheet } from "../components/AiActivity";
import { useSticky } from "../lib/persist";
import { translate as t } from "../i18n";

/*
 * AI models (Hermes phase 1) — Instance Admin. Connections hold declared models; one default model per role.
 * Everything that decides how AI behaves is visible here: readiness per role with reasons, health from real calls,
 * the last test with its exact answer, 24 h latency/failures/fallbacks, and an audit of every change.
 */

type Tab = "models" | "runs" | "audit";

const PROVIDER_LABEL: Record<string, string> = {
  "openai-compatible": "OpenAI-compatible (Ollama, vLLM, LM Studio…)", openai: "OpenAI", anthropic: "Anthropic", deepseek: "DeepSeek",
  systemone: "Decision server (System One)", typesafe: "TypeSafe decision API",
};
const ENDPOINT_HINT: Record<string, string> = {
  "openai-compatible": "http://host:11434/v1", openai: "https://api.openai.com/v1", anthropic: "https://api.anthropic.com", deepseek: "https://api.deepseek.com",
  systemone: "http://host:8080", typesafe: "https://api.typesafe.ai",
};
const KEY_REQUIRED = ["openai", "anthropic", "deepseek", "typesafe"];
const isDecision = (type: string) => type === "systemone" || type === "typesafe";

function setQuery(params: Record<string, string | null>) {
  const u = new URL(window.location.href);
  for (const [k, v] of Object.entries(params)) { if (v) u.searchParams.set(k, v); else u.searchParams.delete(k); }
  window.history.replaceState(window.history.state, "", u.toString());
}

export function AiModelsPage({ me, go }: { me: Me; go: (p: string) => void }) {
  const params = new URLSearchParams(window.location.search);
  const raw = params.get("tab") as Tab;
  const tab: Tab = raw === "runs" || raw === "audit" ? raw : "models";
  const [run, setRun] = useState<string | null>(params.get("run"));
  const openRun = (id: string | null) => { setRun(id); setQuery({ run: id }); };
  if (!me.isInstanceAdmin)
    return (<><PageHeader title={t("AI models")} />
      <EmptyState title={t("Only an Instance Admin can manage AI models")} /></>);
  return (
    <>
      <PageHeader title={t("AI models")} />
      <Tabs<Tab> tabs={[{ key: "models", label: t("Models") }, { key: "runs", label: t("Call log") }, { key: "audit", label: t("Change history") }]}
        value={tab} onChange={(x) => go(`ai?tab=${x}`)} />
      {tab === "models" && <ModelsTab openRun={openRun} />}
      {tab === "runs" && <RunsTab openRun={openRun} />}
      {tab === "audit" && <AuditTab />}
      {run && <RunSheet id={run} onClose={() => openRun(null)} />}
    </>
  );
}

/* ───────── Models tab ───────── */

type Edit = { kind: "connection"; c?: AiConnection } | { kind: "model"; conn: AiConnection; m?: AiModel; suggest?: string } | { kind: "discover"; conn: AiConnection };

function ModelsTab({ openRun }: { openRun: (id: string) => void }) {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ["ai-overview"], queryFn: () => api.ai.getOverview({}), refetchInterval: 10_000 });
  const [edit, setEdit] = useState<Edit | null>(null);
  const [del, setDel] = useState<{ kind: "connection" | "model"; id: string; name: string } | null>(null);
  const refresh = () => void qc.invalidateQueries({ queryKey: ["ai-overview"] });
  const test = useMutation({
    mutationFn: (modelId: string) => api.ai.testModel({ modelId, prompt: "" }),
    onSuccess: (r) => { refresh(); openRun(r.id); },
    onError: refresh,
  });
  const remove = useMutation({
    mutationFn: (d: { kind: string; id: string }) => d.kind === "connection" ? api.ai.deleteConnection({ id: d.id }) : api.ai.deleteModel({ id: d.id }),
    onSuccess: () => { setDel(null); refresh(); },
  });
  const o = q.data;
  if (q.isLoading) return <Skeleton rows={6} />;
  return (
    <>
      <ErrorBox error={q.error ? errorText(q.error) : test.error ? errorText(test.error) : null} onRetry={() => void q.refetch()} />
      {o && <Readiness o={o} />}
      <div className="mb-3 mt-8 flex items-center justify-between">
        <h2 className="text-sm font-semibold">{t("Connections")}</h2>
        <button className="btn-primary" onClick={() => setEdit({ kind: "connection" })}><Plus className="h-4 w-4" />{t("Add connection")}</button>
      </div>
      {o && o.connections.length === 0 && (
        <EmptyState title={t("No AI provider connected yet")} icon={<Plug className="h-6 w-6" />}
          action={<button className="btn-primary" onClick={() => setEdit({ kind: "connection" })}>{t("Add connection")}</button>} />)}
      <div className="space-y-4">
        {o?.connections.map((c) => (
          <ConnectionCard key={c.id} c={c} models={o.models.filter((m) => m.connectionId === c.id)} o={o}
            testing={test.isPending ? test.variables : undefined} onTest={(id) => test.mutate(id)} openRun={openRun}
            onEdit={() => setEdit({ kind: "connection", c })} onDelete={() => setDel({ kind: "connection", id: c.id, name: c.name })}
            onAddModel={() => setEdit({ kind: "model", conn: c })} onDiscover={() => setEdit({ kind: "discover", conn: c })}
            onEditModel={(m) => setEdit({ kind: "model", conn: c, m })} onDeleteModel={(m) => setDel({ kind: "model", id: m.id, name: m.modelId })} />))}
      </div>
      {edit?.kind === "connection" && o && <ConnectionSheet c={edit.c} catalog={o.catalog!} onClose={() => setEdit(null)} onSaved={() => { setEdit(null); refresh(); }} />}
      {edit?.kind === "model" && o && <ModelSheet conn={edit.conn} m={edit.m} suggest={edit.suggest} catalog={o.catalog!} onClose={() => setEdit(null)} onSaved={() => { setEdit(null); refresh(); }} />}
      {edit?.kind === "discover" && o && <DiscoverSheet conn={edit.conn} declared={o.models.filter((m) => m.connectionId === edit.conn.id).map((m) => m.modelId)}
        onClose={() => setEdit(null)} onPick={(id) => setEdit({ kind: "model", conn: edit.conn, suggest: id })} />}
      {del && <ConfirmDialog title={del.kind === "connection" ? t("Delete connection") : t("Delete model")} label={t("Delete")} busy={remove.isPending}
        consequence={<>{del.kind === "connection" ? t("The connection and every model declared on it are removed. Roles that use them lose their default.") : t("The model is removed. Roles that use it lose their default.")}
          {" "}{t("Past calls stay in the log.")}{remove.error && <span className="mt-2 block text-danger">{errorText(remove.error)}</span>}</>}
        confirmText={del.name} onConfirm={() => remove.mutate(del)} onClose={() => setDel(null)} />}
    </>
  );
}

const READY_STYLE: Record<string, { cls: string; icon: typeof CheckCircle2; label: string }> = {
  ready: { cls: "border-emerald-400/40 bg-emerald-500/5", icon: CheckCircle2, label: "Ready" },
  degraded: { cls: "border-amber-400/50 bg-amber-500/5", icon: AlertTriangle, label: "Needs attention" },
  missing: { cls: "border-rose-400/50 bg-rose-500/5", icon: CircleX, label: "Not available" },
};

/** One card per role: is it usable right now, with which model, and why (not). The default is chosen right here. */
function Readiness({ o }: { o: AiOverview }) {
  const qc = useQueryClient();
  const set = useMutation({
    mutationFn: (v: { role: string; modelId: string }) => api.ai.setRoleDefault(v),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["ai-overview"] }),
  });
  const conns = Object.fromEntries(o.connections.map((c) => [c.id, c]));
  return (
    <>
      <ErrorBox error={set.error ? errorText(set.error) : null} />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-5">
        {o.readiness.map((r: AiRoleReadiness) => {
          const st = READY_STYLE[r.state] ?? READY_STYLE.missing;
          const options = o.models.filter((m) => m.roles.includes(r.role));
          return (
            <div key={r.role} className={`card flex flex-col gap-2 ${st.cls}`}>
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-semibold">{t(ROLE_LABEL[r.role] ?? r.role)}</span>
                <span className={`inline-flex items-center gap-1 text-xs font-medium ${r.state === "ready" ? "text-success" : r.state === "degraded" ? "text-warning" : "text-danger"}`}>
                  <st.icon className="h-3.5 w-3.5" />{t(st.label)}</span>
              </div>
              <select className="input text-sm" value={r.modelId} disabled={set.isPending || options.length === 0}
                onChange={(e) => set.mutate({ role: r.role, modelId: e.target.value })} aria-label={t("Default model for {role}", { role: t(ROLE_LABEL[r.role] ?? r.role) })}>
                <option value="">{options.length === 0 ? t("No model serves this role") : t("— No default —")}</option>
                {options.map((m) => <option key={m.id} value={m.id}>{(m.displayName || m.modelId) + " · " + (conns[m.connectionId]?.name ?? "")}</option>)}
              </select>
              <ul className="space-y-0.5 text-xs">
                {r.reasons.map((x, i) => <li key={i} className={r.state === "ready" ? "text-fg-muted" : "text-fg"}>• {t(x)}</li>)}
              </ul>
            </div>);
        })}
      </div>
    </>
  );
}

const HEALTH: Record<string, { cls: string; label: string; icon: typeof CheckCircle2 }> = {
  ok: { cls: "text-success", label: "Healthy", icon: CheckCircle2 },
  unknown: { cls: "text-fg-subtle", label: "Not tested", icon: CircleDashed },
  degraded: { cls: "text-warning", label: "Degraded", icon: AlertTriangle },
  failed: { cls: "text-danger", label: "Failed", icon: CircleX },
};

function Chip({ children, tone }: { children: React.ReactNode; tone?: "role" | "default" }) {
  return <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[11px] ${tone === "role" ? "bg-primary/15 text-primary" : "bg-surface-2 text-fg-muted"}`}>{children}</span>;
}

function ConnectionCard({ c, models, o, testing, onTest, openRun, onEdit, onDelete, onAddModel, onDiscover, onEditModel, onDeleteModel }: {
  c: AiConnection; models: AiModel[]; o: AiOverview; testing?: string; onTest: (id: string) => void; openRun: (id: string) => void;
  onEdit: () => void; onDelete: () => void; onAddModel: () => void; onDiscover: () => void; onEditModel: (m: AiModel) => void; onDeleteModel: (m: AiModel) => void;
}) {
  const [open, setOpen] = useSticky<boolean>(`ai.conn.${c.id}`, true);
  const defaults = Object.fromEntries(o.readiness.filter((r) => r.modelId).map((r) => [r.modelId, [] as string[]]));
  o.readiness.forEach((r) => { if (r.modelId) defaults[r.modelId].push(r.role); });
  return (
    <section className={`card p-0 ${c.isActive ? "" : "opacity-70"}`}>
      <div className="flex flex-wrap items-start justify-between gap-3 px-4 py-3">
        <button className="flex min-w-0 items-start gap-3 text-left" onClick={() => setOpen(!open)} aria-expanded={open}>
          <span className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-surface-2"><Plug className="h-4 w-4 text-fg-muted" /></span>
          <span className="min-w-0">
            <span className="flex flex-wrap items-center gap-2">
              <span className="font-semibold">{c.name}</span>
              <Chip>{t(PROVIDER_LABEL[c.providerType] ?? c.providerType)}</Chip>
              {!c.isActive && <Chip>{t("turned off")}</Chip>}
              {c.isExternal && <span className="badge bg-warning/15 text-warning">{t("external API")}</span>}
              {c.inFlight > 0 && <span className="inline-flex items-center gap-1 text-xs text-info"><Loader2 className="h-3 w-3 animate-spin" />{t("{n} calls running", { n: c.inFlight })}</span>}
            </span>
            <span className="mono mt-0.5 block truncate text-xs text-fg-muted">{c.endpoint}</span>
            <span className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-fg-subtle">
              <span className={`inline-flex items-center gap-1 ${c.hasKey || !KEY_REQUIRED.includes(c.providerType) ? "" : "text-warning"}`}><KeyRound className="h-3 w-3" />
                {c.hasKey ? t("Key saved") : KEY_REQUIRED.includes(c.providerType) ? t("No key — required") : t("No key (not needed)")}</span>
              <span>{t("{n} parallel calls", { n: c.maxConcurrency })}</span>
              <span>{c.rpmLimit > 0 ? t("{n} calls/min", { n: c.rpmLimit }) : t("no rate limit")}</span>
              <span>{t("timeout {s}", { s: fmtMs(c.timeoutMs) })}</span>
              <span>{t("{n} models", { n: models.length })}</span>
            </span>
          </span>
        </button>
        <div className="flex flex-wrap gap-1.5">
          <button className="btn-ghost" onClick={onDiscover}><Search className="h-4 w-4" />{t("Find models")}</button>
          <button className="btn-ghost" onClick={onAddModel}><Plus className="h-4 w-4" />{t("Declare model")}</button>
          <button className="btn-ghost px-2" onClick={onEdit} aria-label={t("Edit connection")} title={t("Edit connection")}><Pencil className="h-4 w-4" /></button>
          <button className="btn-ghost px-2 text-danger" onClick={onDelete} aria-label={t("Delete connection")} title={t("Delete connection")}><Trash2 className="h-4 w-4" /></button>
        </div>
      </div>
      {open && (
        <div className="border-t border-line">
          {models.length === 0 && <div className="px-4 py-6 text-center text-sm text-fg-muted">{t("No model declared on this connection. Use “Find models” or “Declare model”.")}</div>}
          {models.map((m) => {
            const h = HEALTH[m.health] ?? HEALTH.unknown;
            const lt = m.lastTest;
            return (
              <div key={m.id} className={`grid gap-3 border-b border-line px-4 py-3 last:border-0 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,1.2fr)_auto] ${m.isActive ? "" : "opacity-60"}`}>
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-medium">{m.displayName || m.modelId}</span>
                    {m.displayName && <span className="mono text-xs text-fg-subtle">{m.modelId}</span>}
                    {!m.isActive && <Chip>{t("turned off")}</Chip>}
                  </div>
                  <div className="mt-1 flex flex-wrap gap-1">
                    {m.roles.map((r) => <Chip key={r} tone="role">{t(ROLE_LABEL[r] ?? r)}{defaults[m.id]?.includes(r) ? " ★" : ""}</Chip>)}
                    <Chip>{t("in")}: {m.inputs.join(", ")}</Chip>
                    <Chip>{t("out")}: {m.outputs.join(", ")}</Chip>
                    {m.skills.map((s) => <Chip key={s}>{s}</Chip>)}
                    <Chip>{m.tier}</Chip>
                  </div>
                  {defaults[m.id] && <div className="mt-1 text-[11px] text-fg-subtle">★ {t("default for")} {defaults[m.id].map((r) => t(ROLE_LABEL[r] ?? r)).join(", ")}</div>}
                </div>
                <div className="text-xs">
                  <div className={`flex items-center gap-1 font-medium ${h.cls}`}><h.icon className="h-3.5 w-3.5" />{t(h.label)}
                    {m.circuit !== "closed" && <span className="ml-1 rounded bg-danger/15 px-1 text-danger">{t("paused")}</span>}</div>
                  {m.healthReason && <div className="mt-0.5 line-clamp-2 break-words text-fg-muted">{t(m.healthReason)}</div>}
                  {m.healthAt && <div className="text-fg-subtle"><RelTime at={m.healthAt} /></div>}
                  <div className="mt-1 text-fg-subtle">
                    {m.stats && m.stats.runs > 0n
                      ? t("24 h: {runs} calls · {fail} failed · {fb} fallback · p50 {p50} · p95 {p95}", {
                        runs: String(m.stats.runs), fail: String(m.stats.failures), fb: String(m.stats.fallbacks), p50: fmtMs(m.stats.p50Ms), p95: fmtMs(m.stats.p95Ms) })
                      : t("24 h: no calls")}
                  </div>
                  <div className="text-fg-subtle">{m.inputUsdPer1m >= 0 ? t("${i} in / ${o} out per 1M tokens", { i: m.inputUsdPer1m, o: m.outputUsdPer1m }) : t("price not set")}</div>
                </div>
                <div className="min-w-0 text-xs">
                  <div className="mb-1 font-medium text-fg-muted">{t("Last test")}</div>
                  {!lt ? <div className="text-fg-subtle">{t("Never tested")}</div> : (
                    <button className="block w-full rounded-md border border-line px-2 py-1.5 text-left hover:border-primary/40" onClick={() => openRun(lt.id)}>
                      <span className={`flex items-center gap-1 font-medium ${lt.state === "succeeded" ? "text-success" : lt.state === "failed" ? "text-danger" : "text-info"}`}>
                        {lt.state === "succeeded" ? <CheckCircle2 className="h-3.5 w-3.5" /> : lt.state === "failed" ? <CircleX className="h-3.5 w-3.5" /> : <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                        {lt.state === "running" ? t("running") : fmtMs(lt.latencyMs)} · <RelTime at={lt.startedAt} />
                        {lt.trigger === "auto-recovery" && <span className="font-normal text-fg-subtle">· {t("automatic retest")}</span>}
                      </span>
                      <span className="mt-0.5 line-clamp-2 break-words text-fg-muted">{t(lt.summary)}</span>
                    </button>)}
                </div>
                <div className="flex items-start gap-1.5">
                  <button className="btn-ghost" disabled={testing === m.id} onClick={() => onTest(m.id)}>
                    {testing === m.id ? <Loader2 className="h-4 w-4 animate-spin" /> : <FlaskConical className="h-4 w-4" />}{t("Test")}</button>
                  <button className="btn-ghost px-2" onClick={() => onEditModel(m)} aria-label={t("Edit model")} title={t("Edit model")}><Pencil className="h-4 w-4" /></button>
                  <button className="btn-ghost px-2 text-danger" onClick={() => onDeleteModel(m)} aria-label={t("Delete model")} title={t("Delete model")}><Trash2 className="h-4 w-4" /></button>
                </div>
              </div>);
          })}
        </div>)}
    </section>
  );
}

function ConnectionSheet({ c, catalog, onClose, onSaved }: { c?: AiConnection; catalog: AiCatalog; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({
    name: c?.name ?? "", providerType: c?.providerType ?? "openai-compatible", endpoint: c?.endpoint ?? "", apiKey: "",
    maxConcurrency: c?.maxConcurrency ?? 4, rpmLimit: c?.rpmLimit ?? 0, timeoutMs: c?.timeoutMs ?? 60000, isActive: c?.isActive ?? true, clearKey: false,
    isExternal: c?.isExternal ?? KEY_REQUIRED.includes(c?.providerType ?? "openai-compatible"),
  });
  const save = useMutation({
    mutationFn: () => api.ai.saveConnection({ id: c?.id ?? "", name: f.name, providerType: f.providerType, endpoint: f.endpoint,
      apiKey: f.clearKey ? "-" : f.apiKey, maxConcurrency: f.maxConcurrency, rpmLimit: f.rpmLimit, timeoutMs: f.timeoutMs, isActive: f.isActive,
      isExternal: f.isExternal }),
    onSuccess: onSaved,
  });
  const set = <K extends keyof typeof f>(k: K, v: (typeof f)[K]) => setF((x) => ({ ...x, [k]: v }));
  return (
    <Dialog title={c ? t("Edit connection") : t("Add connection")} onClose={onClose}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); save.mutate(); }}>
        <ErrorBox error={save.error ? errorText(save.error) : null} />
        <Field label={t("Name")}><input className="input" value={f.name} onChange={(e) => set("name", e.target.value)} autoFocus required placeholder={t("e.g. Local Ollama")} /></Field>
        <Field label={t("Provider type")}>
          <select className="input" value={f.providerType} disabled={!!c} onChange={(e) => setF((x) => ({ ...x, providerType: e.target.value, isExternal: KEY_REQUIRED.includes(e.target.value) }))}>
            {catalog.providerTypes.map((p) => <option key={p} value={p}>{t(PROVIDER_LABEL[p] ?? p)}</option>)}
          </select>
        </Field>
        <Field label={t("Endpoint (API base URL)")}>
          <input className="input mono" value={f.endpoint} onChange={(e) => set("endpoint", e.target.value)} required placeholder={ENDPOINT_HINT[f.providerType]} />
        </Field>
        <Field label={t("API key")}>
          <input className="input" type="password" autoComplete="off" value={f.apiKey} disabled={f.clearKey} onChange={(e) => set("apiKey", e.target.value)} placeholder={c?.hasKey ? "••••••••" : ""} />
        </Field>
        {c?.hasKey && <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={f.clearKey} onChange={(e) => set("clearKey", e.target.checked)} />{t("Remove the saved key")}</label>}
        <div className="grid grid-cols-3 gap-3">
          <Field label={t("Parallel calls")}><input className="input" type="number" min={1} max={256} value={f.maxConcurrency} onChange={(e) => set("maxConcurrency", Number(e.target.value))} /></Field>
          <Field label={t("Calls / minute")}><input className="input" type="number" min={0} value={f.rpmLimit} onChange={(e) => set("rpmLimit", Number(e.target.value))} /></Field>
          <Field label={t("Timeout (ms)")}><input className="input" type="number" min={500} step={500} value={f.timeoutMs} onChange={(e) => set("timeoutMs", Number(e.target.value))} /></Field>
        </div>
        <label className="flex items-start gap-2 text-sm"><input type="checkbox" className="mt-1" checked={f.isExternal} onChange={(e) => set("isExternal", e.target.checked)} />
          <span>{t("External API — data sent here leaves the platform")}</span></label>
        {c && <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={f.isActive} onChange={(e) => set("isActive", e.target.checked)} /><Power className="h-4 w-4" />{t("Connection is on")}</label>}
        <div className="flex justify-end gap-2 border-t border-line pt-4">
          <button type="button" className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-primary" disabled={save.isPending}>{save.isPending ? t("Saving…") : t("Save")}</button>
        </div>
      </form>
    </Dialog>
  );
}

function Toggles({ all, value, onChange, labels, only }: { all: string[]; value: string[]; onChange: (v: string[]) => void; labels?: Record<string, string>; only?: string[] }) {
  return (
    <div className="flex flex-wrap gap-1.5">
      {all.filter((x) => !only || only.includes(x)).map((x) => {
        const on = value.includes(x);
        return <button type="button" key={x} onClick={() => onChange(on ? value.filter((y) => y !== x) : [...value, x])}
          className={`rounded-md border px-2 py-1 text-xs ${on ? "border-primary bg-primary/15 text-fg" : "border-line text-fg-muted hover:text-fg"}`} aria-pressed={on}>
          {t(labels?.[x] ?? x)}</button>;
      })}
    </div>
  );
}

function ModelSheet({ conn, m, suggest, catalog, onClose, onSaved }: { conn: AiConnection; m?: AiModel; suggest?: string; catalog: AiCatalog; onClose: () => void; onSaved: () => void }) {
  const decision = isDecision(conn.providerType);
  const [f, setF] = useState({
    modelId: m?.modelId ?? suggest ?? "", displayName: m?.displayName ?? "", roles: m?.roles ?? (decision ? ["decision"] : ["analyst"]),
    inputs: m?.inputs ?? ["text"], outputs: m?.outputs ?? (decision ? ["json"] : ["text"]), skills: m?.skills ?? [], tier: m?.tier ?? (decision ? "small" : "standard"),
    inPrice: m && m.inputUsdPer1m >= 0 ? String(m.inputUsdPer1m) : "", outPrice: m && m.outputUsdPer1m >= 0 ? String(m.outputUsdPer1m) : "",
    maxTokens: m?.maxTokens ?? 4096, isActive: m?.isActive ?? true,
  });
  const set = <K extends keyof typeof f>(k: K, v: (typeof f)[K]) => setF((x) => ({ ...x, [k]: v }));
  const save = useMutation({
    mutationFn: () => api.ai.saveModel({ id: m?.id ?? "", connectionId: conn.id, modelId: f.modelId, displayName: f.displayName, roles: f.roles, inputs: f.inputs,
      outputs: f.outputs, skills: f.skills, tier: f.tier, inputUsdPer1m: f.inPrice === "" ? -1 : Number(f.inPrice), outputUsdPer1m: f.outPrice === "" ? -1 : Number(f.outPrice),
      maxTokens: f.maxTokens, isActive: f.isActive }),
    onSuccess: onSaved,
  });
  return (
    <Dialog wide title={m ? t("Edit model") : t("Declare model")} onClose={onClose}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); save.mutate(); }}>
        <ErrorBox error={save.error ? errorText(save.error) : null} />
        <div className="text-sm text-fg-muted">{t("Connection")}: <b className="text-fg">{conn.name}</b></div>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label={t("Model name at the provider")}>
            <input className="input mono" value={f.modelId} disabled={!!m} onChange={(e) => set("modelId", e.target.value)} required autoFocus={!m} />
          </Field>
          <Field label={t("Display name")}><input className="input" value={f.displayName} onChange={(e) => set("displayName", e.target.value)} /></Field>
        </div>
        <Field label={t("Roles")}>
          <Toggles all={catalog.roles} value={f.roles} onChange={(v) => set("roles", v)} labels={ROLE_LABEL} only={decision ? ["decision", "judge"] : undefined} />
        </Field>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label={t("Accepts")}><Toggles all={catalog.inputs} value={f.inputs} onChange={(v) => set("inputs", v)} /></Field>
          <Field label={t("Returns")}><Toggles all={catalog.outputs} value={f.outputs} onChange={(v) => set("outputs", v)} /></Field>
          <Field label={t("Skills")}><Toggles all={catalog.skills} value={f.skills} onChange={(v) => set("skills", v)} /></Field>
          <Field label={t("Tier")}>
            <select className="input" value={f.tier} onChange={(e) => set("tier", e.target.value)}>{catalog.tiers.map((x) => <option key={x}>{x}</option>)}</select>
          </Field>
        </div>
        <div className="grid grid-cols-3 gap-3">
          <Field label={t("Input $ / 1M tokens")}><input className="input" type="number" step="any" min={0} value={f.inPrice} onChange={(e) => set("inPrice", e.target.value)} /></Field>
          <Field label={t("Output $ / 1M tokens")}><input className="input" type="number" step="any" min={0} value={f.outPrice} onChange={(e) => set("outPrice", e.target.value)} /></Field>
          <Field label={t("Max output tokens")}><input className="input" type="number" min={16} value={f.maxTokens} onChange={(e) => set("maxTokens", Number(e.target.value))} /></Field>
        </div>
        {m && <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={f.isActive} onChange={(e) => set("isActive", e.target.checked)} /><Power className="h-4 w-4" />{t("Model is on")}</label>}
        <div className="flex justify-end gap-2 border-t border-line pt-4">
          <button type="button" className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-primary" disabled={save.isPending || f.roles.length === 0}>{save.isPending ? t("Saving…") : t("Save")}</button>
        </div>
      </form>
    </Dialog>
  );
}

function DiscoverSheet({ conn, declared, onClose, onPick }: { conn: AiConnection; declared: string[]; onClose: () => void; onPick: (id: string) => void }) {
  const q = useQuery({ queryKey: ["ai-discover", conn.id], queryFn: () => api.ai.discoverModels({ connectionId: conn.id }), gcTime: 0 });
  const [filter, setFilter] = useState("");
  const ids = (q.data?.modelIds ?? []).filter((x) => x.toLowerCase().includes(filter.toLowerCase()));
  return (
    <Dialog title={t("Models served by {name}", { name: conn.name })} onClose={onClose}>
      <ErrorBox error={q.error ? errorText(q.error) : q.data?.error ? t(q.data.error) : null} onRetry={() => void q.refetch()} />
      {q.isLoading ? <div className="flex items-center gap-2 text-sm text-fg-muted"><Loader2 className="h-4 w-4 animate-spin" />{t("Asking the provider…")}</div> : (
        <>
          {q.data && !q.data.error && <div className="mb-3 text-xs text-fg-subtle">{t("{n} models · answered in {ms}", { n: q.data.modelIds.length, ms: fmtMs(q.data.latencyMs) })}</div>}
          {(q.data?.modelIds.length ?? 0) > 8 && <input className="input mb-3" placeholder={t("Filter…")} value={filter} onChange={(e) => setFilter(e.target.value)} />}
          <div className="space-y-1">
            {ids.map((id) => {
              const done = declared.includes(id);
              return (
                <button key={id} disabled={done} onClick={() => onPick(id)}
                  className="flex w-full items-center justify-between rounded-md border border-line px-3 py-2 text-left text-sm hover:border-primary/40 disabled:opacity-50">
                  <span className="mono truncate">{id}</span>
                  {done ? <span className="text-xs text-fg-subtle">{t("declared")}</span> : <Plus className="h-4 w-4 text-fg-subtle" />}
                </button>);
            })}
          </div>
        </>)}
    </Dialog>
  );
}

/* ───────── Runs tab ───────── */

function RunsTab({ openRun }: { openRun: (id: string) => void }) {
  const [state, setState] = useSticky<string>("ai.runs.state", "");
  const q = useQuery({ queryKey: ["ai-runs", state], queryFn: () => api.ai.listRuns({ workspaceId: "", state, modelId: "", limit: 200 }), refetchInterval: 5000 });
  const filters: [string, string][] = [["", "All"], ["running", "Running"], ["failed", "Failed"], ["fallback", "Fallback used"], ["succeeded", "Succeeded"]];
  return (
    <>
      <div className="mb-4 flex flex-wrap items-center gap-1.5">
        {filters.map(([k, l]) => (
          <button key={k} onClick={() => setState(k)} className={`rounded-md px-2.5 py-1 text-sm ${state === k ? "bg-surface-2 font-medium text-fg" : "text-fg-muted hover:text-fg"}`}>{t(l)}</button>))}
      </div>
      <ErrorBox error={q.error ? errorText(q.error) : null} onRetry={() => void q.refetch()} />
      {q.isLoading ? <Skeleton rows={6} /> : (q.data?.runs.length ?? 0) === 0
        ? <EmptyState title={t("No AI call yet")} icon={<ShieldCheck className="h-6 w-6" />} />
        : <div className="card p-2">{q.data!.runs.map((r) => <RunRow key={r.id} run={r} onOpen={openRun} />)}</div>}
    </>
  );
}

/* ───────── Audit tab ───────── */

function diff(before: string, after: string): { key: string; from: string; to: string }[] {
  const parse = (s: string): Record<string, unknown> => { try { return s ? JSON.parse(s) : {}; } catch { return {}; } };
  const a = parse(before), b = parse(after);
  const keys = [...new Set([...Object.keys(a), ...Object.keys(b)])];
  const show = (v: unknown) => (v === undefined || v === null ? "—" : String(v));
  return keys.filter((k) => show(a[k]) !== show(b[k])).map((k) => ({ key: k, from: show(a[k]), to: show(b[k]) }));
}

const ACTION_LABEL: Record<string, string> = {
  "connection.create": "Added connection", "connection.update": "Changed connection", "connection.delete": "Deleted connection",
  "model.create": "Declared model", "model.update": "Changed model", "model.delete": "Deleted model", "role.default": "Changed role default",
};

function AuditTab() {
  const q = useQuery({ queryKey: ["ai-audit"], queryFn: () => api.ai.listAudit({ limit: 200 }) });
  useEffect(() => { void q.refetch(); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  if (q.isLoading) return <Skeleton rows={6} />;
  const entries = q.data?.entries ?? [];
  return (
    <>
      <ErrorBox error={q.error ? errorText(q.error) : null} onRetry={() => void q.refetch()} />
      {entries.length === 0 ? <EmptyState title={t("No change yet")} /> : (
        <div className="card divide-y divide-line p-0">
          {entries.map((e) => {
            const d = diff(e.beforeJson, e.afterJson);
            return (
              <div key={e.id} className="px-4 py-3 text-sm">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium">{t(ACTION_LABEL[e.action] ?? e.action)}</span>
                  <span className="text-fg-muted">{e.target}</span>
                  <span className="ml-auto text-xs text-fg-subtle">{e.actorName || "system"} · <RelTime at={e.at} /></span>
                </div>
                {d.length > 0 && (
                  <div className="mt-1 flex flex-wrap gap-x-4 gap-y-0.5 text-xs">
                    {d.map((x) => <span key={x.key}><span className="text-fg-subtle">{x.key}:</span> <span className="text-danger line-through">{x.from}</span> → <span className="text-success">{x.to}</span></span>)}
                  </div>)}
              </div>);
          })}
        </div>)}
    </>
  );
}
