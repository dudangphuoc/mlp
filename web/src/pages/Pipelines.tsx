import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { Dialog, EmptyState, ErrorBox, Field, PageHeader, StateBadge , RelTime } from "../components/ui";
import { DataTable } from "../components/DataTable";
import type { PipelineStep } from "../gen/mlplatform/v1/pipeline_pb";
import { translate as t } from "../i18n";
import { PIPELINE_TEMPLATES } from "../lib/pipelineTemplates";
import { Flame, LayoutTemplate } from "lucide-react";

const example = (projectId: string) => JSON.stringify({
  params: { threshold: 0.6 },
  steps: [
    { id: "train", kind: "train", config: { project_id: projectId || "<project-id>", recipe: "toy-recipe", variant: "tiny", params: { epochs: 3 }, cpus: 1 } },
    { id: "gate", kind: "gate", needs: ["train"], config: { left: "${steps.train.metric}", op: ">=", right: "${params.threshold}" } },
    { id: "register", kind: "register", needs: ["gate"], config: { run_id: "${steps.train.run_id}", model_name: "my-model" } },
    { id: "promote", kind: "promote", needs: ["register"], config: { version_id: "${steps.register.version_id}", stage: "staging" } },
  ],
}, null, 2);

const stepColor: Record<string, string> = {
  pending: "#cbd5e1", running: "#38bdf8", succeeded: "#34d399", failed: "#fb7185", skipped: "#e2e8f0", gated: "#fbbf24",
};

/** S-14-1 Pipeline list + recent runs; create a pipeline from a JSON definition. */
export function PipelinesPage({ ws, onRun }: { ws: string; onRun: (runId: string) => void }) {
  const qc = useQueryClient();
  const pipelines = useQuery({ queryKey: ["pipelines", ws], queryFn: () => api.pipeline.listPipelines({ workspaceId: ws }) });
  const runs = useQuery({ queryKey: ["pipeline-runs", ws], queryFn: () => api.pipeline.listRuns({ workspaceId: ws, pipelineId: "" }), refetchInterval: 4000 });
  const projects = useQuery({ queryKey: ["projects", ws], queryFn: () => api.tracking.listProjects({ workspaceId: ws }) });
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ name: "", definition: "", trigger: "manual", minutes: 60 });
  const fromTemplate = (key: string) => {
    const tpl = PIPELINE_TEMPLATES.find((x) => x.key === key);
    if (tpl) setF({ ...f, name: tpl.name, definition: JSON.stringify(tpl.definition, null, 2) });
    setOpen(true);
  };
  const definition = f.definition || example(projects.data?.projects[0]?.id ?? "");
  const create = useMutation({
    mutationFn: () => api.pipeline.createPipeline({
      workspaceId: ws, name: f.name, definitionJson: definition,
      triggerJson: JSON.stringify(f.trigger === "interval" ? { type: "interval", minutes: f.minutes } : f.trigger === "drift" ? { type: "event", event: "deployment.drift" } : { type: "manual" }),
    }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["pipelines", ws] }); setOpen(false); },
  });
  const start = useMutation({
    mutationFn: (id: string) => api.pipeline.startRun({ workspaceId: ws, pipelineId: id, paramsJson: "" }),
    onSuccess: (r) => { qc.invalidateQueries({ queryKey: ["pipeline-runs", ws] }); onRun(r.id); },
  });
  const list = pipelines.data?.pipelines ?? [];
  const triggerText = (raw: string) => { try { const o = JSON.parse(raw); return o.type === "interval" ? t("every {n} min", { n: o.minutes }) : o.type === "event" ? t("on {e}", { e: o.event }) : t("manual"); } catch { return raw; } };
  return (
    <>
      <PageHeader title="Pipelines"
        actions={<>
          <button className="btn-ghost" onClick={() => fromTemplate(PIPELINE_TEMPLATES[0].key)}><LayoutTemplate className="h-4 w-4" />{t("From a template")}</button>
          <button className="btn-primary" onClick={() => setOpen(true)}>+ Pipeline</button>
        </>} />
      <ErrorBox error={pipelines.error ? errorText(pipelines.error) : start.error ? errorText(start.error) : null} />
      {list.length === 0 && !pipelines.isLoading ? <EmptyState title={t("No pipelines yet.")}><button className="btn-primary" onClick={() => setOpen(true)}>{t("Create pipeline")}</button></EmptyState> : (
        <div className="mb-4 grid gap-3 md:grid-cols-3">
          {list.map((p) => {
            let steps: { id: string; kind: string }[] = [];
            try { steps = JSON.parse(p.definitionJson).steps ?? []; } catch { /* show empty */ }
            return (
              <div key={p.id} className="card">
                <div className="flex items-center justify-between"><span className="font-medium">{p.name}</span><span className="text-xs text-slate-500">{triggerText(p.triggerJson)}</span></div>
                <div className="mt-1 text-xs text-slate-500">{steps.map((s) => `${s.id} (${s.kind})`).join(" → ")}</div>
                <button className="btn-ghost mt-2 py-1 text-xs" disabled={start.isPending} onClick={() => start.mutate(p.id)}>{t("Run now")}</button>
              </div>);
          })}
        </div>)}
      <DataTable rows={runs.data?.runs ?? []} loading={runs.isLoading} rowKey={(r) => r.id} onRowClick={(r) => onRun(r.id)} empty={t("No runs yet.")}
        columns={[
          { key: "run", header: t("Runs"), value: (r) => `${r.pipelineName} ${r.number}`, cell: (r) => <span className="font-medium">{r.pipelineName} #{r.number}</span> },
          { key: "state", header: t("Status"), value: (r) => r.state, cell: (r) => <StateBadge state={r.state} reason={r.error || undefined} /> },
          { key: "trigger", header: t("Activate"), value: (r) => r.trigger, cell: (r) => <span className="text-xs">{r.trigger}</span> },
          { key: "error", header: t("Error"), value: (r) => r.error, cell: (r) => <span className="block max-w-xs truncate text-xs text-danger">{r.error}</span> },
          { key: "created", header: t("Start"), value: (r) => Number(r.createdAt?.seconds ?? 0), cell: (r) => <span className="text-xs"><RelTime at={r.createdAt} /></span> },
        ]} />
      {open && (
        <Dialog title={t("Create pipeline")} onClose={() => setOpen(false)} wide>
          <div className="space-y-3">
            <div>
              <div className="label">{t("Start from a template")}</div>
              <div className="grid gap-2 md:grid-cols-2">
                {PIPELINE_TEMPLATES.map((tpl) => (
                  <button key={tpl.key} type="button" onClick={() => fromTemplate(tpl.key)}
                    className={`rounded-lg border p-3 text-left text-sm hover:border-primary ${f.name === tpl.name ? "border-primary bg-primary/5" : "border-line"}`}>
                    <span className="flex items-center gap-2 font-medium"><Flame className="h-4 w-4 text-orange-500" />{t(tpl.title)}</span>
                    <span className="mt-1 block text-xs text-fg-muted">{t(tpl.description)}</span>
                  </button>))}
              </div>
            </div>
            <div className="grid gap-3 md:grid-cols-3">
              <Field label={t("Name")}><input className="input" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
              <Field label={t("Activate")}>
                <select className="input" value={f.trigger} onChange={(e) => setF({ ...f, trigger: e.target.value })}>
                  <option value="manual">{t("Manual")}</option><option value="interval">{t("On a schedule")}</option><option value="drift">{t("When a deployment drifts")}</option>
                </select>
              </Field>
              {f.trigger === "interval" && <Field label={t("Every (minutes)")}><input className="input" type="number" min={5} value={f.minutes} onChange={(e) => setF({ ...f, minutes: Number(e.target.value) })} /></Field>}
            </div>
            <Field label={t("Definition (JSON)")}>
              <textarea className="input h-72 font-mono text-xs" value={definition} onChange={(e) => setF({ ...f, definition: e.target.value })} />
            </Field>
            <ErrorBox error={create.error ? errorText(create.error) : null} />
            <div className="flex justify-end gap-2">
              <button className="btn-ghost" onClick={() => setOpen(false)}>{t("Cancel")}</button>
              <button className="btn-primary" disabled={!f.name || create.isPending} onClick={() => create.mutate()}>{t("Created")}</button>
            </div>
          </div>
        </Dialog>)}
    </>
  );
}

/** Arrange steps by layer (depth in the DAG) to draw left → right. */
function layout(steps: PipelineStep[]) {
  const depth = new Map<string, number>();
  const d = (id: string): number => {
    if (depth.has(id)) return depth.get(id)!;
    const s = steps.find((x) => x.id === id);
    const v = s && s.needs.length ? Math.max(...s.needs.map(d)) + 1 : 0;
    depth.set(id, v);
    return v;
  };
  steps.forEach((s) => d(s.id));
  const cols: string[][] = [];
  steps.forEach((s) => { (cols[depth.get(s.id)!] ??= []).push(s.id); });
  const pos = new Map<string, { x: number; y: number }>();
  cols.forEach((c, i) => c.forEach((id, j) => pos.set(id, { x: 20 + i * 190, y: 20 + j * 80 })));
  return { pos, width: 40 + cols.length * 190, height: 40 + Math.max(1, ...cols.map((c) => c.length)) * 80 };
}

/** S-14-2 Run: step graph by state, step details (output, error, run link), retry from the failed step, cancel. */
export function PipelineRunPage({ ws, id, onBack }: { ws: string; id: string; onBack: () => void }) {
  const qc = useQueryClient();
  const d = useQuery({ queryKey: ["pipeline-run", ws, id], queryFn: () => api.pipeline.getRun({ workspaceId: ws, runId: id }),
    refetchInterval: (q) => (q.state.data?.run?.state === "running" ? 2000 : false) });
  const [sel, setSel] = useState<string | null>(null);
  const [params, setParams] = useState("");
  const refresh = () => qc.invalidateQueries({ queryKey: ["pipeline-run", ws, id] });
  const retry = useMutation({ mutationFn: () => api.pipeline.retryRun({ workspaceId: ws, runId: id, paramsJson: params }), onSuccess: refresh });
  const cancel = useMutation({ mutationFn: () => api.pipeline.cancelRun({ workspaceId: ws, runId: id }), onSuccess: refresh });
  const steps = d.data?.steps ?? [];
  const { pos, width, height } = useMemo(() => layout(steps), [steps]);
  const r = d.data?.run;
  if (!r) return <div className="p-6 text-slate-500">{d.error ? errorText(d.error) : "Loading…"}</div>;
  const step = steps.find((s) => s.id === sel);
  return (
    <>
      <button className="mb-3 text-sm text-brand-500" onClick={onBack}>← Pipelines</button>
      <PageHeader title={`${r.pipelineName} #${r.number}`} subtitle={<span className="flex items-center gap-2"><StateBadge state={r.state} />{r.trigger} · {t("params")} {r.paramsJson}</span>}
        actions={<>
          {r.state === "running" && <button className="btn-ghost" onClick={() => cancel.mutate()}>{t("Cancel")}</button>}
        </>} />
      <ErrorBox error={retry.error ? errorText(retry.error) : cancel.error ? errorText(cancel.error) : r.error || null} />
      {(r.state === "failed" || r.state === "cancelled") && (
        <div className="card mb-4 flex items-end gap-2">
          <Field label={t("Rerun from the failed step — override parameters (JSON, optional)")}><input className="input font-mono text-xs" value={params} placeholder='{"threshold":0.5}' onChange={(e) => setParams(e.target.value)} /></Field>
          <button className="btn-primary" disabled={retry.isPending} onClick={() => retry.mutate()}>{t("Start again")}</button>
        </div>)}
      <div className="grid gap-4 md:grid-cols-3">
        <div className="card overflow-x-auto md:col-span-2">
          <svg width={width} height={height} role="img" aria-label={t("Pipeline step graph")}>
            {steps.flatMap((s) => s.needs.map((n) => {
              const a = pos.get(n)!, b = pos.get(s.id)!;
              return <path key={n + s.id} d={`M${a.x + 150},${a.y + 25} C${a.x + 170},${a.y + 25} ${b.x - 20},${b.y + 25} ${b.x},${b.y + 25}`} fill="none" stroke="#94a3b8" strokeWidth={1.5} />;
            }))}
            {steps.map((s) => {
              const p = pos.get(s.id)!;
              return (
                <g key={s.id} transform={`translate(${p.x},${p.y})`} className="cursor-pointer" onClick={() => setSel(s.id)}>
                  <rect width={150} height={50} rx={8} fill="white" stroke={sel === s.id ? "#3e63dd" : stepColor[s.state] ?? "#cbd5e1"} strokeWidth={sel === s.id ? 2.5 : 2} />
                  <rect width={6} height={50} rx={3} fill={stepColor[s.state] ?? "#cbd5e1"} />
                  <text x={14} y={21} fontSize={13} fontWeight={600} fill="#0f172a">{s.id}</text>
                  <text x={14} y={38} fontSize={11} fill="#64748b">{s.kind} · {s.state}{s.attempt > 1 ? ` · ${t("try {n}", { n: s.attempt })}` : ""}</text>
                </g>);
            })}
          </svg>
        </div>
        <div className="card text-sm">
          {step ? (
            <div className="space-y-2">
              <div className="flex items-center justify-between"><span className="font-medium">{step.id}</span><StateBadge state={step.state} /></div>
              <div className="text-xs text-slate-500">{step.kind}{step.needs.length ? ` · ${t("needs {x}", { x: step.needs.join(", ") })}` : ""}</div>
              {step.error && <div className="rounded bg-rose-50 p-2 text-xs text-rose-700">{step.error}</div>}
              <pre className="overflow-x-auto rounded bg-slate-900 p-2 text-xs text-slate-100">{JSON.stringify(JSON.parse(step.outputJson || "{}"), null, 2)}</pre>
              {step.kind === "train" && (() => {
                const o = JSON.parse(step.outputJson || "{}") as { run_id?: string; project_id?: string };
                return o.run_id && o.project_id ? <a className="text-xs text-brand-500" href={`${window.location.pathname.split("/").slice(0, 3).join("/")}/projects/${o.project_id}/runs/${o.run_id}`}>{t("View run ↗")}</a> : null;
              })()}
            </div>
          ) : <div className="text-slate-500">{t("Select a step to see its output and errors.")}</div>}
        </div>
      </div>
    </>
  );
}
