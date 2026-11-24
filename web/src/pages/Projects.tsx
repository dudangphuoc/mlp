import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CookingPot, Play, Square } from "lucide-react";
import { api, errorText } from "../lib/api";
import { ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, PageHeader, RelTime, StateBadge, Tabs, Tip } from "../components/ui";
import { CollectionView, DataTable } from "../components/DataTable";
import { CHART_COLORS, MetricChart, metricText } from "../components/MetricChart";
import { dur, ProgressLine, reasonText, secs, useNow } from "../components/JobProgress";
import { cookAgain, RecipeCanvas } from "../components/RecipeCanvas";
import { RunComparison } from "../components/RunComparison";
import { CodeEntries } from "../components/CodeEntries";
import { translate as t } from "../i18n";
import { useCrumb } from "../lib/crumbs";
import { useSticky } from "../lib/persist";
import type { Run } from "../gen/mlplatform/v1/tracking_pb";
import type { ModelVersion } from "../gen/mlplatform/v1/registry_pb";
import { StageControl } from "../components/ModelStage";
import { ExportSheet, type ExportTarget } from "../components/ModelExport";
import { ModelTestSheet, type TestTarget } from "../components/ModelTester";

/** S-09-1 Projects. */
export function ProjectsPage({ ws, onOpen }: { ws: string; onOpen: (id: string) => void }) {
  const qc = useQueryClient();
  const projects = useQuery({ queryKey: ["projects", ws], queryFn: () => api.tracking.listProjects({ workspaceId: ws }), refetchInterval: 15_000 });
  const tasks = useQuery({ queryKey: ["tasks"], queryFn: () => api.catalog.listTasks({}) });
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ name: "", slug: "", task: "detect" });
  const create = useMutation({
    mutationFn: () => api.tracking.createProject({ workspaceId: ws, ...f }),
    onSuccess: (p) => { qc.invalidateQueries({ queryKey: ["projects", ws] }); setOpen(false); onOpen(p.id); },
  });
  const list = projects.data?.projects ?? [];
  return (
    <>
      <PageHeader title="Projects"
        actions={<button className="btn-primary" onClick={() => setOpen(true)}>+ Project</button>} />
      {list.length === 0 && !projects.isLoading ? <EmptyState title={t("No projects yet")} action={<button className="btn-primary" onClick={() => setOpen(true)}>{t("Create project")}</button>} /> : (
        <CollectionView rows={list} loading={projects.isLoading} rowKey={(p) => p.id} onOpen={(p) => onOpen(p.id)}
          filters={[{ key: "task", label: "Task", options: [...new Set(list.map((p) => p.task))].map((t) => ({ value: t, label: t })), test: (p, v) => p.task === v }]}
          card={(p) => (
            <button className="card flex h-full w-full flex-col text-left transition hover:border-primary/50" onClick={() => onOpen(p.id)}>
              <div className="flex items-start justify-between gap-2"><span className="font-medium">{p.name}</span><span className="badge bg-surface-2 text-fg-muted">{p.task}</span></div>
              <div className="mt-2 text-sm">
                {p.runCount === 0 ? <span className="text-fg-subtle">{t("No training runs yet")}</span>
                  : <>{t("{n} runs", { n: p.runCount })}{p.activeCount > 0 && <span className="text-info"> · {t("{n} active", { n: p.activeCount })}</span>}</>}
              </div>
              {p.hasBest && <div className="mt-1 truncate text-xs text-fg-muted">{t("best")} <span className="font-mono">{p.bestMetricKey}</span> <b className="text-success">{metricText(p.bestMetricValue)}</b> · #{p.bestRunNumber} {p.bestRunName}</div>}
              <div className="mt-auto pt-2 text-xs text-fg-subtle">{p.lastRunAt ? <>{t("last run")} <RelTime at={p.lastRunAt} /></> : <>{t("created")} <RelTime at={p.createdAt} /></>}</div>
            </button>)}
          columns={[
            { key: "name", header: t("Name"), value: (p) => p.name, cell: (p) => <span className="font-medium">{p.name}</span> },
            { key: "task", header: "Task", value: (p) => p.task, cell: (p) => p.task },
            { key: "runs", header: t("Training runs"), align: "right", value: (p) => p.runCount, cell: (p) => <span className="tabular-nums">{p.runCount}{p.activeCount > 0 && <span className="text-info"> · {t("{n} active", { n: p.activeCount })}</span>}</span> },
            { key: "best", header: t("Best result"), value: (p) => (p.hasBest ? p.bestMetricValue : undefined), cell: (p) => (p.hasBest ? <span className="whitespace-nowrap"><b className="tabular-nums text-success">{metricText(p.bestMetricValue)}</b> <span className="text-xs text-fg-muted">{p.bestMetricKey} · #{p.bestRunNumber}</span></span> : <span className="text-fg-subtle">—</span>) },
            { key: "last", header: t("Last run"), value: (p) => Number(p.lastRunAt?.seconds ?? 0), cell: (p) => <RelTime at={p.lastRunAt} /> },
            { key: "created", header: t("Created"), value: (p) => Number(p.createdAt?.seconds ?? 0), cell: (p) => <RelTime at={p.createdAt} /> },
          ]} />)}
      {open && (
        <Dialog title={t("Create project")} onClose={() => setOpen(false)}>
          <div className="space-y-3">
            <Field label={t("Name")}><input className="input" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value, slug: e.target.value.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") })} /></Field>
            <Field label={t("Path")}><input className="input" value={f.slug} onChange={(e) => setF({ ...f, slug: e.target.value })} /></Field>
            <Field label={t("Problem (task)")}>
              <select className="input" value={f.task} onChange={(e) => setF({ ...f, task: e.target.value })}>
                {(tasks.data?.tasks ?? []).map((t) => <option key={t.key} value={t.key}>{t.key} ({t.modalityIn} → {t.modalityOut})</option>)}
              </select>
            </Field>
            <ErrorBox error={create.error ? errorText(create.error) : null} />
            <button className="btn-primary" disabled={!f.name || !f.slug || create.isPending} onClick={() => create.mutate()}>{t("Create project")}</button>
          </div>
        </Dialog>)}
    </>
  );
}

const FINISHED = ["succeeded", "failed", "cancelled"];
/** A run is "queued" until its first metric arrives; before that the job state says what is really happening. */
export const runState = (r: Run) => (FINISHED.includes(r.state) ? r.state : r.jobState || r.state);
export const runActive = (r: Run) => !FINISHED.includes(r.state);
export const runSeconds = (r: Run, now: number) => (r.startedAt ? (r.endedAt ? secs(r.endedAt) : runActive(r) ? now : secs(r.startedAt)) - secs(r.startedAt) : 0);
/** Loss-like metrics improve downwards; everything else (mAP, accuracy, F1…) upwards. */
export const lowerIsBetter = (key: string) => /(loss|error|wer|cer|rmse|mae|mse|perplexity)/i.test(key);
/** "Fine-tune" or "Train again", plus the starting point when it is not the variant's pretrained weights. */
export const backboneOnly = (r: Run) => r.startKind === "backbone" || (!r.startKind && r.fromScratch);   // runs from before revisions only carry from_scratch
export const runKind = (r: Run) => r.origin === "code" ? t("Code") : `${r.trainKind === "retrain" ? t("Train again") + (r.retrainOfNumber > 0 ? ` #${r.retrainOfNumber}` : "") : t("Fine-tune")}${backboneOnly(r) ? ` · ${t("backbone only")}` : ""}`;

type PTab = "recipe" | "repo" | "runs" | "models";
const PTABS: PTab[] = ["recipe", "repo", "runs", "models"];

function parseParams(json: string): Record<string, unknown> {
  try { const o = JSON.parse(json || "{}"); return o && typeof o === "object" ? o : {}; } catch { return {}; }
}

/** The model versions registered from this project's runs: which run made them, their stage (changeable here) and their metrics. */
function ProjectModels({ ws, projectId, runs, onRun, go }: { ws: string; projectId: string; runs: Run[]; onRun: (runId: string) => void; go: (path: string) => void }) {
  const [test, setTest] = useState<TestTarget | null>(null);
  const [convert, setConvert] = useState<ExportTarget | null>(null);
  const all = useQuery({
    queryKey: ["project-versions", ws, projectId], refetchInterval: 10000,
    queryFn: async () => {
      const models = (await api.registry.listModels({ workspaceId: ws })).models;
      return (await Promise.all(models.map((m) => api.registry.listModelVersions({ workspaceId: ws, modelId: m.id })))).flatMap((r) => r.versions);
    },
  });
  const byRun = useMemo(() => new Map(runs.map((r) => [r.id, r])), [runs]);
  const rows = useMemo(() => (all.data ?? []).filter((v) => byRun.has(v.runId)).sort((a, b) => Number(b.createdAt?.seconds ?? 0) - Number(a.createdAt?.seconds ?? 0)), [all.data, byRun]);
  const metrics = (v: ModelVersion) => Object.entries(parseParams(v.metricsJson) as Record<string, number>).filter(([k]) => k.startsWith("val/") || k.startsWith("test/")).slice(0, 3);
  if (all.error) return <ErrorBox error={errorText(all.error)} onRetry={() => void all.refetch()} />;
  if (!all.isLoading && rows.length === 0) return <EmptyState title={t("No models yet")} />;
  return (
    <>
      <DataTable rows={rows} loading={all.isLoading} rowKey={(v) => v.id} search={false}
        columns={[
          { key: "model", header: "Model", value: (v) => v.modelName, cell: (v) => <button className="font-medium hover:text-primary" onClick={() => go(`models?model=${v.modelId}`)}>{v.modelName} <span className="text-fg-subtle">v{v.version}</span></button> },
          { key: "run", header: t("Training run"), value: (v) => byRun.get(v.runId)?.number ?? 0, cell: (v) => <button className="text-primary hover:underline" onClick={() => onRun(v.runId)}>#{byRun.get(v.runId)?.number}</button> },
          { key: "stage", header: "Stage", value: (v) => v.stage, cell: (v) => <StageControl ws={ws} version={v} /> },
          { key: "metrics", header: "Metric", cell: (v) => <div className="mono text-xs">{metrics(v).map(([k, x]) => <div key={k}>{k}={Number(x).toFixed(4)}</div>)}</div> },
          { key: "created", header: t("Created"), value: (v) => Number(v.createdAt?.seconds ?? 0), cell: (v) => <span className="text-xs"><RelTime at={v.createdAt} /></span> },
          { key: "act", header: "", align: "right", cell: (v) => (
            <span className="inline-flex gap-1 whitespace-nowrap">
              <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setTest({ kind: "version", versionId: v.id, title: `${v.modelName} v${v.version}`, task: v.task })}>{t("Test")}</button>
              <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setConvert({ versionId: v.id, title: `${v.modelName} v${v.version}` })}>{t("Convert")}</button>
              {["detect", "classify"].includes(v.task) && v.source !== "optimize" && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => go(`optimize?version=${v.id}`)}>{t("Optimize")}</button>}
            </span>) },
        ]} />
      {test && <ModelTestSheet ws={ws} target={test} onClose={() => setTest(null)} />}
      {convert && <ExportSheet ws={ws} target={convert} onClose={() => setConvert(null)} onJob={(id) => go(`jobs/${id}`)} />}
    </>
  );
}

/** S-09-2 Project: the recipe canvas (what the next training will do), every run identifiable at a glance and comparable, and the way to its models. */
export function ProjectPage({ ws, id, onBack, onRun, go }: { ws: string; id: string; onBack: () => void; onRun: (runId: string) => void; go: (path: string) => void }) {
  const qc = useQueryClient();
  const projects = useQuery({ queryKey: ["projects", ws], queryFn: () => api.tracking.listProjects({ workspaceId: ws }) });
  const project = projects.data?.projects.find((p) => p.id === id);
  useCrumb(id, project?.name);
  const runs = useQuery({ queryKey: ["runs", ws, id], queryFn: () => api.tracking.listRuns({ workspaceId: ws, projectId: id }), refetchInterval: 4000 });
  const list = useMemo(() => runs.data?.runs ?? [], [runs.data]);
  const now = useNow();
  const [tab, setTabState] = useState<PTab>(() => { const q = new URLSearchParams(window.location.search).get("tab") as PTab; return PTABS.includes(q) ? q : "recipe"; });
  const setTab = (v: PTab) => {
    setTabState(v);
    const u = new URL(window.location.href);
    if (v === "recipe") u.searchParams.delete("tab"); else u.searchParams.set("tab", v);
    window.history.replaceState(window.history.state, "", u);
  };
  const [stopping, setStopping] = useState<Run | null>(null);
  const [stateFilter, setStateFilter] = useSticky(`project:${id}:state`, "");
  const [picked, setPicked] = useSticky<string[] | null>(`project:${id}:compare`, null);

  const primaryKey = list.find((r) => r.primaryMetricKey)?.primaryMetricKey ?? "";
  const lower = lowerIsBetter(primaryKey);
  const scored = list.filter((r) => r.hasPrimaryMetric);
  const best = scored.reduce<Run | null>((b, r) => (!b || (lower ? r.primaryMetricValue < b.primaryMetricValue : r.primaryMetricValue > b.primaryMetricValue) ? r : b), null);
  // Compared runs: the user's picks, otherwise the five most recent runs that have a result.
  const compare = useMemo(() => (picked ? list.filter((r) => picked.includes(r.id)) : scored.slice(0, 5)).slice(0, CHART_COLORS.length), [picked, list]); // eslint-disable-line react-hooks/exhaustive-deps
  const colorOf = (r: Run) => { const i = compare.findIndex((x) => x.id === r.id); return i < 0 ? undefined : CHART_COLORS[i]; };
  const toggle = (r: Run) => setPicked((compare.some((x) => x.id === r.id) ? compare.filter((x) => x.id !== r.id) : [...compare, r]).map((x) => x.id));

  const series = useQuery({
    queryKey: ["compare", ws, primaryKey, compare.map((r) => r.id + r.state + (runActive(r) ? Math.floor(now / 10) : "")).join()],
    enabled: compare.length > 0 && !!primaryKey, placeholderData: (old) => old,
    queryFn: async () => Promise.all(compare.map(async (r) => {
      const m = await api.tracking.getMetrics({ workspaceId: ws, runId: r.id, keys: [primaryKey], maxPoints: 300 });
      return { name: `#${r.number} ${r.name}`, color: colorOf(r), points: (m.series[0]?.points ?? []).map((p) => [Number(p.step), p.value] as [number, number]) };
    })),
  });

  // Parameters worth a column: the ones whose value differs between runs (what the user actually changed).
  const paramsOf = useMemo(() => new Map(list.map((r) => [r.id, parseParams(r.paramsJson)])), [list]);
  const diffKeys = useMemo(() => {
    const keys = new Set<string>(); for (const p of paramsOf.values()) Object.keys(p).forEach((k) => keys.add(k));
    const differing = [...keys].filter((k) => new Set([...paramsOf.values()].map((p) => JSON.stringify(p[k] ?? null))).size > 1);
    return (differing.length > 0 ? differing : [...keys]).slice(0, 4);
  }, [paramsOf]);

  const stop = useMutation({
    mutationFn: (r: Run) => api.tracking.stopRun({ workspaceId: ws, runId: r.id }),
    onSuccess: () => { setStopping(null); void qc.invalidateQueries({ queryKey: ["runs", ws, id] }); },
  });

  const cook = useMutation({
    mutationFn: (r: Run) => cookAgain(ws, r),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["project-recipes", ws, id] }); setTab("recipe"); },
  });

  const active = list.filter(runActive).length;
  const rows = list.filter((r) => !stateFilter || (stateFilter === "active" ? runActive(r) : runState(r) === stateFilter));
  const hasChart = (series.data ?? []).some((s) => s.points.length > 0);

  return (
    <>
      <button className="mb-3 text-sm text-primary" onClick={onBack}>← {t("Projects")}</button>
      <PageHeader title={project?.name ?? t("Project")}
        subtitle={<span>{t("task {task}", { task: project?.task ?? "" })} · {t("{n} runs", { n: list.length })}{active > 0 && <> · <span className="text-info">{t("{n} active", { n: active })}</span></>}
          {best && <> · {t("best {k} {v} (run #{n})", { k: primaryKey, v: metricText(best.primaryMetricValue), n: best.number })}</>}</span>}
        actions={tab !== "recipe" ? <button className="btn-primary" onClick={() => setTab("recipe")}><Play className="h-4 w-4" />{t("New training")}</button> : undefined} />
      <ErrorBox error={runs.error ? errorText(runs.error) : stop.error ? errorText(stop.error) : cook.error ? errorText(cook.error) : null} onRetry={() => void runs.refetch()} />
      <Tabs<PTab> value={tab} onChange={setTab} tabs={[{ key: "recipe", label: t("Recipe") }, { key: "repo", label: "Repo" }, { key: "runs", label: t("Run history"), count: list.length }, { key: "models", label: t("Models") }]} />

      {tab === "recipe" && (
        <>
          {list.filter(runActive).slice(0, 3).map((r) => (
            <button key={r.id} className="mb-3 flex w-full items-center gap-2 rounded-lg border border-primary/40 bg-primary/10 px-3 py-2 text-left text-sm hover:border-primary" onClick={() => onRun(r.id)}>
              <span className="h-2 w-2 shrink-0 animate-pulse rounded-full bg-primary" />
              <span className="min-w-0 truncate">{t("training #{n} is running — open", { n: r.number })}</span>
              <span className="ml-auto shrink-0 truncate text-xs text-fg-muted">{r.name}</span>
            </button>))}
          {project && !runs.isLoading ? <RecipeCanvas ws={ws} projectId={id} task={project.task} runs={list} go={go} onStarted={(run) => onRun(run.id)} /> : <div className="skeleton h-64" />}
        </>)}

      {tab === "repo" && <CodeEntries ws={ws} projectId={id} onRun={onRun} />}

      {tab === "models" && <ProjectModels ws={ws} projectId={id} runs={list} onRun={onRun} go={go} />}

      {tab === "runs" && (list.length === 0 && !runs.isLoading ? (
        <EmptyState title={t("No training runs yet")} action={<button className="btn-primary" onClick={() => setTab("recipe")}><Play className="h-4 w-4" />{t("New training")}</button>} />
      ) : (
        <>
          {primaryKey && compare.length > 0 && (
            <div className="mb-4">
              {hasChart && <MetricChart title={t("{k} by step", { k: primaryKey })} series={series.data ?? []} height={240} />}
              <RunComparison ws={ws} runs={compare} colorOf={colorOf} />
            </div>)}
          <DataTable rows={rows} loading={runs.isLoading} rowKey={(r) => r.id} onRowClick={(r) => onRun(r.id)} empty={t("No run matches this filter.")}
            actions={
              <select className="input h-8 w-auto" value={stateFilter} onChange={(e) => setStateFilter(e.target.value)} aria-label={t("Status")}>
                <option value="">{t("Status: all")}</option><option value="active">{t("active")}</option>
                {["succeeded", "failed", "cancelled"].map((x) => <option key={x} value={x}>{t(x === "succeeded" ? "done" : x)}</option>)}
              </select>}
            columns={[
              { key: "cmp", header: "", width: "2rem", cell: (r) => (
                <Tip text={r.hasPrimaryMetric ? t("Show in the comparison chart") : t("No result to compare yet")}>
                  <input type="checkbox" className="h-4 w-4" style={{ accentColor: colorOf(r) }} disabled={!r.hasPrimaryMetric} checked={!!colorOf(r)}
                    onClick={(e) => e.stopPropagation()} onChange={() => toggle(r)} aria-label={t("Compare run #{n}", { n: r.number })} />
                </Tip>) },
              { key: "run", header: t("Training run"), value: (r) => `${r.number} ${r.name} ${r.recipe} ${r.variant}`, cell: (r) => (
                <div className="min-w-0">
                  <div className="flex items-center gap-2"><span className="tabular-nums text-fg-subtle">#{r.number}</span><span className="truncate font-medium">{r.name}</span>
                    {best?.id === r.id && scored.length > 1 && <span className="badge bg-emerald-100 text-emerald-700">{t("best")}</span>}</div>
                  <div className="text-xs text-fg-muted">{r.origin === "code" ? <span className="font-mono">{r.code?.script || r.hostName}</span> : <>{r.recipe} / {r.variant}</>}</div>
                </div>) },
              { key: "state", header: t("Status"), value: (r) => runState(r), cell: (r) => <RunStatus r={r} /> },
              { key: "metric", header: primaryKey || t("Result"), align: "right", value: (r) => (r.hasPrimaryMetric ? r.primaryMetricValue : undefined),
                cell: (r) => <span className={`tabular-nums ${best?.id === r.id ? "font-semibold text-success" : ""}`}>{r.hasPrimaryMetric ? metricText(r.primaryMetricValue) : "—"}</span> },
              { key: "data", header: t("Data"), value: (r) => r.datasetName, cell: (r) => (r.datasetName
                ? <span className="whitespace-nowrap text-xs">{r.datasetName} <span className="text-fg-subtle">v{r.datasetVersionNumber}</span></span> : <span className="text-fg-subtle">—</span>) },
              { key: "rev", header: t("Revision"), value: (r) => r.revisionNumber, cell: (r) => (r.revisionNumber > 0 ? <span className="font-mono text-xs">r{r.revisionNumber}</span> : <span className="text-fg-subtle">—</span>) },
              { key: "kind", header: t("Kind"), value: (r) => runKind(r), cell: (r) => <span className="whitespace-nowrap text-xs">{runKind(r)}</span> },
              { key: "params", header: t("Parameters"), value: (r) => r.paramsJson, cell: (r) => {
                const p = paramsOf.get(r.id) ?? {};
                const shown = diffKeys.filter((k) => p[k] !== undefined);
                return shown.length === 0 ? <span className="text-xs text-fg-subtle">{t("defaults")}</span>
                  : <span className="flex flex-wrap gap-1">{shown.map((k) => <span key={k} className="whitespace-nowrap rounded bg-surface-2 px-1.5 py-0.5 font-mono text-[11px] text-fg-muted">{k}={String(p[k])}</span>)}</span>;
              } },
              { key: "dur", header: t("Duration"), align: "right", value: (r) => runSeconds(r, now), cell: (r) => <span className="whitespace-nowrap text-xs text-fg-muted">{dur(runSeconds(r, now))}</span> },
              { key: "created", header: t("Created"), value: (r) => secs(r.createdAt), cell: (r) => <span className="text-xs"><RelTime at={r.createdAt} />{r.createdBy && <span className="block text-fg-subtle">{r.createdBy}</span>}</span> },
              { key: "actions", header: "", align: "right", cell: (r) => (
                <span className="inline-flex gap-1" onClick={(e) => e.stopPropagation()}>
                  {runActive(r) && <Tip text={t("Stop this run")}><button className="btn-ghost h-8 px-2" aria-label={t("Stop this run")} onClick={() => setStopping(r)}><Square className="h-4 w-4" /></button></Tip>}
                  <Tip text={t("Cook again from #{n}", { n: r.number })}><button className="btn-ghost h-8 px-2" aria-label={t("Cook again from #{n}", { n: r.number })} disabled={cook.isPending} onClick={() => cook.mutate(r)}><CookingPot className="h-4 w-4" /></button></Tip>
                </span>) },
            ]} />
        </>))}

      {stopping && <StopRunSheet run={stopping} busy={stop.isPending} onClose={() => setStopping(null)} onConfirm={() => stop.mutate(stopping)} />}
    </>
  );
}

/** State badge with what the user needs next to it: live progress while it runs, the reason while it waits or after it failed. */
export function RunStatus({ r }: { r: Run }) {
  const st = runState(r);
  const reason = st === "succeeded" ? "" : reasonText(r.stateReason);
  return (
    <div className="w-56 max-w-full">
      <StateBadge state={st} />
      {runActive(r) && r.progress ? <div className="mt-1.5"><ProgressLine progress={r.progress} compact /></div>
        : reason ? <div className={`mt-1 truncate text-xs ${st === "failed" ? "text-danger" : "text-fg-muted"}`} title={reason}>{reason}</div> : null}
    </div>
  );
}

export function StopRunSheet({ run, busy, onClose, onConfirm }: { run: Run; busy: boolean; onClose: () => void; onConfirm: () => void }) {
  return <ConfirmDialog title={t("Stop run #{n}?", { n: run.number })} label="Stop run" busy={busy} onClose={onClose} onConfirm={onConfirm}
    consequence={t("Training stops within about 30 seconds and the GPU is released. Metrics and checkpoints saved so far are kept; the run is marked cancelled.")} />;
}
