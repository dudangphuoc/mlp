import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { api, errorText } from "../lib/api";
import { ago } from "../lib/format";
import { Dialog, EmptyState, ErrorBox, Field, PageHeader, Tabs } from "../components/ui";
import type { LlmCallResponse } from "../gen/mlplatform/v1/llmops_pb";
import type { Deployment } from "../gen/mlplatform/v1/serving_pb";
import { translate as t } from "../i18n";
import { useCrumb } from "../lib/crumbs";

type Tab = "prompts" | "guardrails" | "eval" | "usage";

/** LLM tools of one `generate` deployment: prompts (versions, compare), guardrails, test-set eval, token usage. */
export function DeploymentLlmPage({ ws, id, onBack }: { ws: string; id: string; onBack: () => void }) {
  const deps = useQuery({ queryKey: ["deployments", ws], queryFn: () => api.serving.listDeployments({ workspaceId: ws }) });
  const dep = deps.data?.deployments.find((d) => d.id === id);
  useCrumb(id, dep?.slug);
  const [tab, setTab] = useState<Tab>("prompts");
  return (
    <>
      <button className="mb-3 text-sm text-primary" onClick={onBack}>← Deployments</button>
      <PageHeader title={dep?.slug ?? ""} subtitle={dep && `${dep.modelName} v${dep.modelVersion} · ${dep.task}`} />
      {deps.data && (!dep || dep.task !== "generate") ? <EmptyState title={t("Page not found.")} /> : dep && (
        <>
          <Tabs value={tab} onChange={setTab} tabs={[
            { key: "prompts", label: "Prompt" }, { key: "guardrails", label: "Guardrail" }, { key: "eval", label: "Eval" }, { key: "usage", label: t("Tokens & cost") }]} />
          {/* Tabs stay mounted so a picked prompt and typed variables survive switching tabs. */}
          <div hidden={tab !== "prompts"}><Prompts ws={ws} dep={dep} /></div>
          <div hidden={tab !== "guardrails"}><Guardrails ws={ws} dep={dep} /></div>
          <div hidden={tab !== "eval"}><Evals ws={ws} dep={dep} /></div>
          {tab === "usage" && <Usage ws={ws} dep={dep} />}
        </>)}
    </>
  );
}

const pre = "whitespace-pre-wrap rounded bg-surface-2 p-2 text-xs";

function Prompts({ ws, dep }: { ws: string; dep: Deployment }) {
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ["prompts", ws], queryFn: () => api.llm.listPrompts({ workspaceId: ws }) });
  const [sel, setSel] = useState("");
  const detail = useQuery({ queryKey: ["prompt", ws, sel], enabled: !!sel, queryFn: () => api.llm.getPrompt({ workspaceId: ws, prompt: sel }) });
  const [newName, setNewName] = useState<string | null>(null);
  const [draft, setDraft] = useState<{ system: string; template: string; temperature: number; maxTokens: number; note: string } | null>(null);
  const [vars, setVars] = useState<Record<string, string>>({});
  const [cmp, setCmp] = useState<[number, number]>([0, 0]);
  const [out, setOut] = useState<Record<number, LlmCallResponse | string>>({});
  const create = useMutation({ mutationFn: (name: string) => api.llm.createPrompt({ workspaceId: ws, name, description: "" }),
    onSuccess: (p) => { qc.invalidateQueries({ queryKey: ["prompts", ws] }); setNewName(null); setSel(p.id); } });
  const saveVersion = useMutation({
    mutationFn: () => api.llm.createPromptVersion({ workspaceId: ws, prompt: sel, deploymentId: dep.id, ...draft! }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["prompt", ws, sel] }); qc.invalidateQueries({ queryKey: ["prompts", ws] }); setDraft(null); },
  });
  const versions = detail.data?.versions ?? [];
  const allVars = useMemo(() => [...new Set(versions.flatMap((v) => v.variables))], [versions]);
  async function run(v: number) {
    setOut((o) => ({ ...o, [v]: t("Running…") }));
    try { const r = await api.llm.runPrompt({ workspaceId: ws, prompt: sel, version: v, variables: vars, deploymentId: dep.id }); setOut((o) => ({ ...o, [v]: r })); }
    catch (e) { setOut((o) => ({ ...o, [v]: errorText(e) })); }
  }
  const shown = cmp[0] || cmp[1] ? cmp.filter((x) => x > 0) : versions.slice(0, 2).map((v) => v.version);
  const healthy = dep.state === "healthy";
  return (
    <div className="grid gap-4 md:grid-cols-4">
      <div className="card p-2">
        <div className="mb-1 flex items-center justify-between px-1 text-sm font-medium"><span>Prompt</span>
          <button className="btn-ghost h-7 px-2" onClick={() => setNewName("")} aria-label={t("Create prompt")}><Plus className="h-4 w-4" /></button></div>
        {(list.data?.prompts ?? []).map((p) => (
          <button key={p.id} className={`block w-full rounded px-2 py-1 text-left text-sm ${sel === p.id ? "bg-primary/15 text-fg" : "text-fg-muted hover:bg-surface-2"}`} onClick={() => { setSel(p.id); setOut({}); setCmp([0, 0]); }}>
            {p.name} <span className="text-xs text-fg-subtle">v{p.latestVersion}</span>
          </button>))}
        {list.data?.prompts.length === 0 && <div className="px-1 text-xs text-fg-muted">{t("No prompts yet.")}</div>}
      </div>
      <div className="space-y-4 md:col-span-3">
        {!sel ? <EmptyState title={t("Select or create a prompt.")} /> : (
          <>
            <div className="card">
              <div className="mb-2 flex items-center justify-between text-sm font-medium">
                <span>{detail.data?.prompt?.name} · {t("{n} versions", { n: versions.length })}</span>
                <button className="btn-ghost py-1 text-xs" onClick={() => setDraft({ system: versions[0]?.system ?? "", template: versions[0]?.template ?? "",
                  temperature: versions[0]?.temperature ?? 0.7, maxTokens: versions[0]?.maxTokens ?? 512, note: "" })}>{t("+ New version")}</button>
              </div>
              {versions.length > 0 && (
                <div className="flex flex-wrap items-end gap-2">
                  {allVars.map((v) => <Field key={v} label={`{{${v}}}`}><input className="input" value={vars[v] ?? ""} onChange={(e) => setVars({ ...vars, [v]: e.target.value })} /></Field>)}
                  <Field label={t("Compare versions")}>
                    <div className="flex gap-1">
                      {[0, 1].map((i) => (
                        <select key={i} className="input w-24" value={shown[i] ?? 0} onChange={(e) => { const c: [number, number] = [shown[0] ?? 0, shown[1] ?? 0]; c[i] = Number(e.target.value); setCmp(c); }}>
                          <option value={0}>—</option>{versions.map((v) => <option key={v.version} value={v.version}>v{v.version}</option>)}
                        </select>))}
                    </div>
                  </Field>
                  <button className="btn-primary" disabled={!healthy} title={healthy ? undefined : t(dep.state)} onClick={() => shown.forEach((v) => run(v))}>{t("Run")}</button>
                </div>)}
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              {shown.map((vn) => {
                const v = versions.find((x) => x.version === vn);
                const r = out[vn];
                return (
                  <div key={vn} className="card space-y-2 text-sm">
                    <div className="font-medium">v{vn} <span className="text-xs text-fg-subtle">{v?.note} · {v ? ago(v.createdAt!) : ""}</span></div>
                    {v?.system && <pre className={pre}><b>system:</b> {v.system}</pre>}
                    <pre className={pre}>{v?.template}</pre>
                    {typeof r === "string" ? <div className="text-xs text-fg-muted">{r}</div> : r && (
                      <div className="space-y-1">
                        {r.blocked ? <div className="rounded bg-danger/10 p-2 text-xs text-danger">{t("Blocked:")} {r.blockedReason}</div>
                          : <pre className={`${pre} border border-line`}>{r.output}</pre>}
                        <div className="text-xs text-fg-muted">{r.promptTokens} + {r.completionTokens} token · {r.latencyMs.toFixed(0)} ms{r.cost ? ` · ≈ ${r.cost.toFixed(5)}` : ""}{r.flags.length ? ` · ${r.flags.join(", ")}` : ""}</div>
                      </div>)}
                  </div>);
              })}
            </div>
          </>)}
      </div>
      {newName !== null && (
        <Dialog title={t("Create prompt")} onClose={() => setNewName(null)}>
          <Field label={t("Name")}><input className="input" value={newName} onChange={(e) => setNewName(e.target.value)} /></Field>
          <ErrorBox error={create.error ? errorText(create.error) : null} />
          <button className="btn-primary mt-3" disabled={!newName || create.isPending} onClick={() => create.mutate(newName)}>{t("Create")}</button>
        </Dialog>)}
      {draft && (
        <Dialog title={t("New version")} onClose={() => setDraft(null)} wide>
          <div className="space-y-3">
            <Field label="System"><textarea className="input h-16" value={draft.system} onChange={(e) => setDraft({ ...draft, system: e.target.value })} /></Field>
            <Field label="Template"><textarea className="input h-28 font-mono text-xs" value={draft.template} onChange={(e) => setDraft({ ...draft, template: e.target.value })} /></Field>
            <div className="grid gap-3 md:grid-cols-3">
              <Field label="Temperature"><input className="input" type="number" step="0.1" value={draft.temperature} onChange={(e) => setDraft({ ...draft, temperature: Number(e.target.value) })} /></Field>
              <Field label="Max tokens"><input className="input" type="number" value={draft.maxTokens} onChange={(e) => setDraft({ ...draft, maxTokens: Number(e.target.value) })} /></Field>
              <Field label={t("Note")}><input className="input" value={draft.note} onChange={(e) => setDraft({ ...draft, note: e.target.value })} /></Field>
            </div>
            <ErrorBox error={saveVersion.error ? errorText(saveVersion.error) : null} />
            <button className="btn-primary" disabled={!draft.template || saveVersion.isPending} onClick={() => saveVersion.mutate()}>{t("Save version")}</button>
          </div>
        </Dialog>)}
    </div>
  );
}

function Guardrails({ ws, dep }: { ws: string; dep: Deployment }) {
  const s = useQuery({ queryKey: ["guardrails", ws, dep.id], queryFn: () => api.llm.getGuardrails({ workspaceId: ws, deploymentId: dep.id }) });
  const [f, setF] = useState<{ piiMode: string; blockInjection: boolean; terms: string; checkOutput: boolean; price: number } | null>(null);
  const cur = f ?? (s.data ? { piiMode: s.data.piiMode, blockInjection: s.data.blockInjection, terms: s.data.blockedTerms.join("\n"), checkOutput: s.data.checkOutput, price: s.data.pricePer1k } : null);
  const save = useMutation({ mutationFn: () => api.llm.updateGuardrails({ workspaceId: ws, deploymentId: dep.id, piiMode: cur!.piiMode, blockInjection: cur!.blockInjection,
    blockedTerms: cur!.terms.split("\n").map((x) => x.trim()).filter(Boolean), checkOutput: cur!.checkOutput, pricePer1k: cur!.price }), onSuccess: () => { setF(null); s.refetch(); } });
  return (
    <div className="card max-w-2xl space-y-3">
      <ErrorBox error={s.error ? errorText(s.error) : null} />
      {cur && (
        <>
          <Field label={t("Personal data (email, phone, ID, card)")}><select className="input" value={cur.piiMode} onChange={(e) => setF({ ...cur, piiMode: e.target.value })}>
            <option value="off">{t("No check")}</option><option value="redact">{t("Mask before sending to the model")}</option><option value="block">{t("Block request")}</option></select></Field>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={cur.blockInjection} onChange={(e) => setF({ ...cur, blockInjection: e.target.checked })} />{t("Block prompt injection")}</label>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={cur.checkOutput} onChange={(e) => setF({ ...cur, checkOutput: e.target.checked })} />{t("Also check model output")}</label>
          <Field label={t("Banned words (one per line)")}><textarea className="input h-24" value={cur.terms} onChange={(e) => setF({ ...cur, terms: e.target.value })} /></Field>
          <Field label={t("Estimated cost / 1,000 tokens")}><input className="input" type="number" step="0.001" value={cur.price} onChange={(e) => setF({ ...cur, price: Number(e.target.value) })} /></Field>
          <ErrorBox error={save.error ? errorText(save.error) : null} />
          <button className="btn-primary" disabled={save.isPending || !f} onClick={() => save.mutate()}>{t("Save")}</button>
        </>)}
    </div>
  );
}

function Evals({ ws, dep }: { ws: string; dep: Deployment }) {
  const qc = useQueryClient();
  const sets = useQuery({ queryKey: ["eval-sets", ws], queryFn: () => api.llm.listEvalSets({ workspaceId: ws }) });
  const runs = useQuery({ queryKey: ["eval-runs", ws], queryFn: () => api.llm.listEvalRuns({ workspaceId: ws }) });
  const prompts = useQuery({ queryKey: ["prompts", ws], queryFn: () => api.llm.listPrompts({ workspaceId: ws }) });
  const [f, setF] = useState({ setId: "", prompt: "", version: 0 });
  const promptVersions = useQuery({ queryKey: ["prompt", ws, f.prompt], enabled: !!f.prompt, queryFn: () => api.llm.getPrompt({ workspaceId: ws, prompt: f.prompt }) });
  const [newSet, setNewSet] = useState<{ name: string; cases: string } | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const run = useMutation({ mutationFn: () => api.llm.runEval({ workspaceId: ws, ...f, deploymentId: dep.id }), onSuccess: (r) => { qc.invalidateQueries({ queryKey: ["eval-runs", ws] }); setOpen(r.id); } });
  const create = useMutation({ mutationFn: () => api.llm.createEvalSet({ workspaceId: ws, name: newSet!.name, casesJson: newSet!.cases }), onSuccess: () => { qc.invalidateQueries({ queryKey: ["eval-sets", ws] }); setNewSet(null); } });
  return (
    <div className="space-y-4">
      <div className="card flex flex-wrap items-end gap-2">
        <Field label={t("Test set")}><select className="input" value={f.setId} onChange={(e) => setF({ ...f, setId: e.target.value })}>
          <option value="">—</option>{(sets.data?.sets ?? []).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></Field>
        <Field label="Prompt"><select className="input" value={f.prompt} onChange={(e) => setF({ ...f, prompt: e.target.value, version: 0 })}>
          <option value="">—</option>{(prompts.data?.prompts ?? []).map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select></Field>
        <Field label={t("Version")}><select className="input w-28" value={f.version} onChange={(e) => setF({ ...f, version: Number(e.target.value) })}>
          <option value={0}>{t("latest")}</option>{(promptVersions.data?.versions ?? []).map((v) => <option key={v.version} value={v.version}>v{v.version}</option>)}</select></Field>
        <button className="btn-primary" disabled={!f.setId || !f.prompt || run.isPending || dep.state !== "healthy"} onClick={() => run.mutate()}>{run.isPending ? t("Running…") : t("Run eval")}</button>
        <button className="btn-ghost ml-auto" onClick={() => setNewSet({ name: "", cases: '[\n  {"vars": {"text": "hello"}, "expect": {"contains": "hello", "max_chars": 200}}\n]' })}>{t("+ Test set")}</button>
      </div>
      <ErrorBox error={run.error ? errorText(run.error) : null} />
      <div className="card p-0">
        <table className="w-full text-sm">
          <thead><tr className="text-left text-xs text-fg-muted"><th className="p-3">{t("Test set")}</th><th>Prompt</th><th>{t("Score")}</th><th>{t("At")}</th></tr></thead>
          <tbody>{(runs.data?.runs ?? []).map((r) => (
            <tr key={r.id} className="cursor-pointer border-t border-line hover:bg-surface-2" onClick={() => setOpen(open === r.id ? null : r.id)}>
              <td className="p-3">{r.setName}</td><td>{r.prompt} v{r.promptVersion}</td>
              <td className={r.score >= 0.8 ? "text-emerald-500" : r.score >= 0.5 ? "text-amber-500" : "text-danger"}>{(r.score * 100).toFixed(0)}%</td>
              <td className="text-xs text-fg-muted">{r.createdAt ? ago(r.createdAt) : ""}</td>
            </tr>))}</tbody>
        </table>
        {runs.data?.runs.length === 0 && <div className="p-4 text-sm text-fg-muted">{t("No eval runs yet.")}</div>}
      </div>
      {open && (() => {
        const r = runs.data?.runs.find((x) => x.id === open);
        if (!r) return null;
        const rows = JSON.parse(r.resultsJson) as { index: number; output: string; passed: boolean; checks: Record<string, unknown> }[];
        return (
          <div className="card space-y-1 text-xs">
            {rows.map((x) => (
              <div key={x.index} className={`rounded p-2 ${x.passed ? "bg-emerald-500/10" : "bg-danger/10"}`}>
                <b>#{x.index}</b> {x.passed ? t("pass") : t("fail")} · {Object.entries(x.checks).map(([k, v]) => `${k}=${String(v)}`).join(", ")}
                <div className="font-mono">{x.output || t("(empty)")}</div>
              </div>))}
          </div>);
      })()}
      {newSet && (
        <Dialog title={t("New test set")} onClose={() => setNewSet(null)} wide>
          <div className="space-y-3">
            <Field label={t("Name")}><input className="input" value={newSet.name} onChange={(e) => setNewSet({ ...newSet, name: e.target.value })} /></Field>
            <Field label="Case (JSON)"><textarea className="input h-48 font-mono text-xs" value={newSet.cases} onChange={(e) => setNewSet({ ...newSet, cases: e.target.value })} /></Field>
            <ErrorBox error={create.error ? errorText(create.error) : null} />
            <button className="btn-primary" disabled={!newSet.name || create.isPending} onClick={() => create.mutate()}>{t("Create")}</button>
          </div>
        </Dialog>)}
    </div>
  );
}

function Usage({ ws, dep }: { ws: string; dep: Deployment }) {
  const u = useQuery({ queryKey: ["llm-usage", ws], queryFn: () => api.llm.getUsage({ workspaceId: ws, days: 30 }) });
  const days = (u.data?.days ?? []).filter((d) => d.deploymentId === dep.id);
  const total = days.reduce((a, d) => ({ calls: a.calls + Number(d.calls), tokens: a.tokens + Number(d.promptTokens + d.completionTokens), cost: a.cost + d.cost, blocked: a.blocked + Number(d.blocked) }),
    { calls: 0, tokens: 0, cost: 0, blocked: 0 });
  return (
    <>
      <div className="mb-4 grid gap-3 md:grid-cols-4">
        {[["Calls", total.calls.toLocaleString()], ["Tokens", total.tokens.toLocaleString()], ["Estimated cost", total.cost.toFixed(4)], ["Blocked by guardrail", total.blocked.toLocaleString()]].map(([k, v]) => (
          <div key={k} className="card"><div className="text-xs text-fg-muted">{t(k)}</div><div className="mt-1 text-xl font-semibold">{v}</div></div>))}
      </div>
      <div className="card p-0">
        <table className="w-full text-sm">
          <thead><tr className="text-left text-xs text-fg-muted"><th className="p-3">{t("Day")}</th><th>{t("Calls")}</th><th>{t("Input tokens")}</th><th>{t("Output tokens")}</th><th>{t("Mean latency")}</th><th>{t("Cost")}</th><th>{t("Blocked")}</th></tr></thead>
          <tbody>{days.map((d) => (
            <tr key={String(d.day?.seconds)} className="border-t border-line">
              <td className="p-3">{d.day ? new Date(Number(d.day.seconds) * 1000).toLocaleDateString() : ""}</td><td>{String(d.calls)}</td>
              <td>{String(d.promptTokens)}</td><td>{String(d.completionTokens)}</td><td>{d.avgLatencyMs.toFixed(0)} ms</td><td>{d.cost.toFixed(4)}</td><td>{String(d.blocked)}</td>
            </tr>))}</tbody>
        </table>
        {days.length === 0 && <div className="p-4 text-sm text-fg-muted">{t("No LLM calls yet.")}</div>}
      </div>
    </>
  );
}
