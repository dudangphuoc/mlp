import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { Dialog, EmptyState, ErrorBox, Field, PageHeader, StateBadge , RelTime } from "../components/ui";
import { DataTable } from "../components/DataTable";
import { MetricChart } from "../components/MetricChart";
import { translate as t } from "../i18n";

type ParamDef = { type?: string; default?: unknown; min?: number; max?: number; enum?: unknown[]; from?: string };

/** Suggest a search space from the recipe's parameter declarations: enum → values; number → range around the default. */
function suggestSpace(params: Record<string, ParamDef>): string {
  const out: Record<string, unknown> = {};
  for (const [k, d] of Object.entries(params)) {
    if (d.from) continue;
    if (d.enum) out[k] = { values: d.enum };
    else if (d.type === "integer" && typeof d.default === "number") out[k] = { values: [Math.max(1, Math.round(d.default / 2)), d.default, d.default * 2] };
    else if (d.type === "number" && typeof d.default === "number" && d.default > 0) out[k] = { min: d.default / 10, max: d.default * 10, log: true };
  }
  return JSON.stringify(out, null, 2);
}

/** S-10-1 Sweep list + create sweep (search space, algorithm, budget). */
export function SweepsPage({ ws, onOpen }: { ws: string; onOpen: (id: string) => void }) {
  const list = useQuery({ queryKey: ["sweeps", ws], queryFn: () => api.tuning.listSweeps({ workspaceId: ws }), refetchInterval: 5000 });
  const [open, setOpen] = useState(false);
  const sweeps = list.data?.sweeps ?? [];
  return (
    <>
      <PageHeader title="Sweeps (HPO)"
        actions={<button className="btn-primary" onClick={() => setOpen(true)}>+ Sweep</button>} />
      <ErrorBox error={list.error ? errorText(list.error) : null} />
      {sweeps.length === 0 && !list.isLoading ? (
        <EmptyState title={t("No sweeps yet.")}><button className="btn-primary" onClick={() => setOpen(true)}>{t("Create sweep")}</button></EmptyState>
      ) : (
        <DataTable rows={sweeps} loading={list.isLoading} rowKey={(s) => s.id} onRowClick={(s) => onOpen(s.id)}
          columns={[
            { key: "name", header: t("Name"), value: (s) => s.name, cell: (s) => <span className="font-medium">{s.name}</span> },
            { key: "state", header: t("Status"), value: (s) => s.state, cell: (s) => <StateBadge state={s.state} /> },
            { key: "recipe", header: "Recipe", value: (s) => s.recipe, cell: (s) => <span className="text-fg-muted">{s.recipe} · {s.variant}</span> },
            { key: "algo", header: t("Algorithm"), value: (s) => s.algorithm, cell: (s) => `${s.algorithm} · ${s.maxTrials} trial` },
            { key: "obj", header: t("Objective"), value: (s) => s.objective, cell: (s) => <span className="text-xs">{s.direction === "maximize" ? "↑" : "↓"} {s.objective}</span> },
            { key: "best", header: t("Best"), align: "right", value: (s) => s.bestValue ?? -Infinity, cell: (s) => (s.bestValue !== undefined ? `${s.bestValue.toFixed(4)} (#${s.bestTrial})` : "—") },
            { key: "created", header: t("Created"), value: (s) => Number(s.createdAt?.seconds ?? 0), cell: (s) => <span className="text-xs"><RelTime at={s.createdAt} /></span> },
          ]} />)}
      {open && <CreateSweepDialog ws={ws} onClose={() => setOpen(false)} onCreated={onOpen} />}
    </>
  );
}

function CreateSweepDialog({ ws, onClose, onCreated }: { ws: string; onClose: () => void; onCreated: (id: string) => void }) {
  const qc = useQueryClient();
  const projects = useQuery({ queryKey: ["projects", ws], queryFn: () => api.tracking.listProjects({ workspaceId: ws }) });
  const [projectId, setProjectId] = useState("");
  const project = projects.data?.projects.find((p) => p.id === projectId);
  const recipes = useQuery({ queryKey: ["recipes", ws, project?.task], enabled: !!project,
    queryFn: () => api.catalog.listRecipes({ workspaceId: ws, task: project!.task }) });
  const [recipeName, setRecipe] = useState("");
  const recipe = useQuery({ queryKey: ["recipe", ws, recipeName], enabled: !!recipeName, queryFn: () => api.catalog.getRecipe({ workspaceId: ws, name: recipeName }) });
  const manifest = useMemo(() => (recipe.data?.manifestJson ? JSON.parse(recipe.data.manifestJson) : null), [recipe.data]);
  const [f, setF] = useState({ name: "", variant: "", algorithm: "random", space: "", maxTrials: 10, parallel: 2, objective: "", direction: "maximize", imageOverride: "" });
  const pickRecipe = (name: string) => { setRecipe(name); setF((x) => ({ ...x, space: "" })); };
  const space = f.space || (manifest ? suggestSpace(manifest.params ?? {}) : "");
  const create = useMutation({
    mutationFn: () => api.tuning.createSweep({
      workspaceId: ws, projectId, name: f.name, recipe: recipeName, variant: f.variant || recipe.data?.variants[0]?.name || "", task: project?.task ?? "",
      algorithm: f.algorithm, spaceJson: space, baseParamsJson: "{}", objective: f.objective, direction: f.direction, maxTrials: f.maxTrials,
      parallel: f.parallel, seed: Math.floor(Math.random() * 100000), cpus: 1, gpus: 0, imageOverride: f.imageOverride,
    }),
    onSuccess: (s) => { qc.invalidateQueries({ queryKey: ["sweeps", ws] }); onClose(); onCreated(s.id); },
  });
  const trainable = (recipes.data?.recipes ?? []).filter((r) => r.entrypoints.includes("train"));
  return (
    <Dialog title={t("Create sweep")} onClose={onClose} wide>
      <div className="space-y-3">
        <div className="grid gap-3 md:grid-cols-3">
          <Field label="Project">
            <select className="input" value={projectId} onChange={(e) => { setProjectId(e.target.value); setRecipe(""); }}>
              <option value="">{t("— pick —")}</option>
              {(projects.data?.projects ?? []).map((p) => <option key={p.id} value={p.id}>{p.name} ({p.task})</option>)}
            </select>
          </Field>
          <Field label="Recipe">
            <select className="input" value={recipeName} disabled={!project} onChange={(e) => pickRecipe(e.target.value)}>
              <option value="">{t("— pick —")}</option>
              {trainable.map((r) => <option key={r.name} value={r.name}>{r.displayName || r.name}</option>)}
            </select>
          </Field>
          <Field label={t("Variant")}>
            <select className="input" value={f.variant} disabled={!recipe.data} onChange={(e) => setF({ ...f, variant: e.target.value })}>
              {(recipe.data?.variants ?? []).map((v) => <option key={v.name} value={v.name}>{v.name}</option>)}
            </select>
          </Field>
        </div>
        <Field label={t("Search space (JSON)")}>
          <textarea className="input h-36 font-mono text-xs" value={space} onChange={(e) => setF({ ...f, space: e.target.value })} />
        </Field>
        <div className="grid gap-3 md:grid-cols-4">
          <Field label={t("Algorithm")}>
            <select className="input" value={f.algorithm} onChange={(e) => setF({ ...f, algorithm: e.target.value })}>
              <option value="random">Random</option><option value="grid">Grid</option>
            </select>
          </Field>
          <Field label={t("Max trials")}><input className="input" type="number" min={1} max={500} value={f.maxTrials} onChange={(e) => setF({ ...f, maxTrials: Number(e.target.value) })} /></Field>
          <Field label={t("Parallel")}><input className="input" type="number" min={1} max={16} value={f.parallel} onChange={(e) => setF({ ...f, parallel: Number(e.target.value) })} /></Field>
          <Field label={t("Direction")}>
            <select className="input" value={f.direction} onChange={(e) => setF({ ...f, direction: e.target.value })}>
              <option value="maximize">{t("Max")}</option><option value="minimize">{t("Min")}</option>
            </select>
          </Field>
        </div>
        <div className="grid gap-3 md:grid-cols-2">
          <Field label={t("Objective metric")}>
            <input className="input" value={f.objective} onChange={(e) => setF({ ...f, objective: e.target.value })} />
          </Field>
          <Field label={t("Image (dev only)")}>
            <input className="input" value={f.imageOverride} onChange={(e) => setF({ ...f, imageOverride: e.target.value })} />
          </Field>
        </div>
        <ErrorBox error={create.error ? errorText(create.error) : null} />
        <div className="flex justify-end gap-2">
          <button className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-primary" disabled={!projectId || !recipeName || create.isPending} onClick={() => create.mutate()}>{t("Create sweep")}</button>
        </div>
      </div>
    </Dialog>
  );
}

/** S-10-2 Sweep page: trials table, best metric by trial, stop. */
export function SweepPage({ ws, id, onBack }: { ws: string; id: string; onBack: () => void }) {
  const qc = useQueryClient();
  const d = useQuery({ queryKey: ["sweep", ws, id], queryFn: () => api.tuning.getSweep({ workspaceId: ws, sweepId: id }),
    refetchInterval: (q) => (q.state.data?.sweep?.state === "running" ? 3000 : false) });
  const stop = useMutation({ mutationFn: () => api.tuning.stopSweep({ workspaceId: ws, sweepId: id }), onSuccess: () => qc.invalidateQueries({ queryKey: ["sweep", ws, id] }) });
  const s = d.data?.sweep;
  const trials = d.data?.trials ?? [];
  const series = useMemo(() => {
    const done = trials.filter((t) => t.value !== undefined).sort((a, b) => a.number - b.number);
    let best: number | undefined;
    const pts: [number, number][] = [];
    const raw: [number, number][] = [];
    for (const t of done) {
      const v = t.value!;
      best = best === undefined ? v : s?.direction === "minimize" ? Math.min(best, v) : Math.max(best, v);
      pts.push([t.number, best]); raw.push([t.number, v]);
    }
    return [{ name: "best so far", points: pts }, { name: "trial value", points: raw }];
  }, [trials, s?.direction]);
  if (!s) return <div className="p-6 text-slate-500">{d.error ? errorText(d.error) : "Loading…"}</div>;
  const keys = [...new Set(trials.flatMap((t) => Object.keys(JSON.parse(t.paramsJson || "{}"))))];
  return (
    <>
      <button className="mb-3 text-sm text-brand-500" onClick={onBack}>← Sweeps</button>
      <PageHeader title={s.name} subtitle={<span className="flex items-center gap-2"><StateBadge state={s.state} />{s.recipe} · {s.algorithm} · {trials.length}/{s.maxTrials} trial · {s.direction === "maximize" ? "↑" : "↓"} {s.objective}</span>}
        actions={s.state === "running" && <button className="btn-ghost" onClick={() => stop.mutate()}>{t("Stop")}</button>} />
      <ErrorBox error={stop.error ? errorText(stop.error) : s.stateReason || null} />
      <div className="mb-4 grid gap-4 md:grid-cols-3">
        <div className="card">
          <div className="text-xs text-slate-500">{t("Best trial")}</div>
          <div className="mt-1 text-2xl font-semibold">{s.bestValue !== undefined ? s.bestValue.toFixed(4) : "—"}</div>
          {s.bestTrial !== undefined && <div className="mt-1 font-mono text-xs text-slate-500">#{s.bestTrial} {trials.find((t) => t.number === s.bestTrial)?.paramsJson}</div>}
        </div>
        <div className="md:col-span-2"><MetricChart title={t("{metric} by trial", { metric: s.objective })} series={series.map((x) => ({ ...x, name: t(x.name) }))} height={180} /></div>
      </div>
      <div className="card p-0">
        <table className="w-full text-sm">
          <thead><tr className="text-left text-xs text-slate-500"><th className="p-3">#</th>{keys.map((k) => <th key={k}>{k}</th>)}<th>{t("Status")}</th><th>{s.objective}</th><th /></tr></thead>
          <tbody>
            {trials.map((t) => {
              const p = JSON.parse(t.paramsJson || "{}") as Record<string, unknown>;
              return (
                <tr key={t.number} className={`border-t border-slate-100 ${t.number === s.bestTrial ? "bg-emerald-50" : ""}`}>
                  <td className="p-3">{t.number}</td>
                  {keys.map((k) => <td key={k} className="font-mono text-xs">{String(p[k])}</td>)}
                  <td><StateBadge state={t.state} />{t.error && <div className="text-xs text-rose-600">{t.error}</div>}</td>
                  <td>{t.value !== undefined ? t.value.toFixed(4) : "—"}</td>
                  <td className="pr-3 text-right">{t.runId && <a className="text-xs text-brand-500" href={`${window.location.pathname.split("/").slice(0, 3).join("/")}/projects/${s.projectId}/runs/${t.runId}`}>run ↗</a>}</td>
                </tr>);
            })}
          </tbody>
        </table>
      </div>
    </>
  );
}
