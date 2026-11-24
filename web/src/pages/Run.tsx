import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronRight, Clock, CookingPot, Copy, Download, FlaskConical, Gauge, Pencil, RotateCw, Search, Square, Trash2 } from "lucide-react";
import { api, errorText } from "../lib/api";
import { bytes, dateTime } from "../lib/format";
import { ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, PageHeader, RelTime, ShortId, StateBadge, Tabs } from "../components/ui";
import { MetricChart, metricText } from "../components/MetricChart";
import { dur, errorExcerpt, EventList, LogPanel, PhaseBar, ProgressLine, reasonText, secs, useJobLog, useNow } from "../components/JobProgress";
import { cookAgain, retrainFrom, type ParamDef } from "../components/RecipeCanvas";
import { TrainingLive, trainDetail } from "../components/TrainingLive";
import { ModelTestSheet, type TestTarget } from "../components/ModelTester";
import { EvaluateSheet, type EvalTarget } from "../components/Evaluation";
import { backboneOnly, lowerIsBetter, runActive, runKind, runSeconds, runState, StopRunSheet } from "./Projects";
import { translate as t } from "../i18n";
import { useCrumb } from "../lib/crumbs";
import { useSticky } from "../lib/persist";
import type { MetricInfo, Run } from "../gen/mlplatform/v1/tracking_pb";

type Tab = "overview" | "metrics" | "log" | "config" | "files";
const TABS: Tab[] = ["overview", "metrics", "log", "config", "files"];
const groupOf = (key: string) => (key.includes("/") ? key.slice(0, key.indexOf("/")) : t("other"));

/** S-09-3 Run: follow it while it trains, understand why it failed, read the result, reuse its settings — without leaving the page. */
export function RunPage({ ws, id, onBack, go }: { ws: string; id: string; onBack: () => void; go: (path: string) => void }) {
  const qc = useQueryClient();
  const run = useQuery({
    queryKey: ["run", ws, id], queryFn: () => api.tracking.getRun({ workspaceId: ws, runId: id }),
    refetchInterval: (q) => (q.state.data && !runActive(q.state.data) ? false : 3000),
  });
  const r = run.data;
  const active = !!r && runActive(r);
  const st = r ? runState(r) : "";
  const now = useNow();
  const projects = useQuery({ queryKey: ["projects", ws], queryFn: () => api.tracking.listProjects({ workspaceId: ws }) });
  useCrumb(id, r ? `#${r.number} ${r.name}` : undefined);
  useCrumb(r?.projectId, projects.data?.projects.find((p) => p.id === r?.projectId)?.name);

  const [tab, setTabState] = useState<Tab>(() => { const q = new URLSearchParams(window.location.search).get("tab") as Tab; return TABS.includes(q) ? q : "overview"; });
  const setTab = (v: Tab) => {
    setTabState(v);
    const u = new URL(window.location.href);
    if (v === "overview") u.searchParams.delete("tab"); else u.searchParams.set("tab", v);
    window.history.replaceState(window.history.state, "", u);
  };

  const ops = useQuery({
    queryKey: ["job-ops", ws, r?.jobId], enabled: !!r?.jobId, refetchInterval: active ? 3000 : false,
    queryFn: () => api.scheduler.getJobOps({ workspaceId: ws, jobId: r!.jobId }),
  });
  const [logTail, setLogTail] = useState(2000);
  const lines = useJobLog(ws, r?.jobId || undefined, logTail);
  const summary = useQuery({
    queryKey: ["metric-summary", ws, id, r?.state], enabled: !!r, refetchInterval: active ? 8000 : false,
    queryFn: () => api.tracking.listMetricKeys({ workspaceId: ws, runId: id }),
  });
  const metrics = summary.data?.metrics ?? [];
  const files = useQuery({ queryKey: ["run-files", ws, id, r?.state], enabled: !!r && !active, queryFn: () => api.tracking.listArtifacts({ workspaceId: ws, runId: id }) });
  const map = useQuery({ queryKey: ["cluster-map", ws], queryFn: () => api.ops.getClusterMap({ workspaceId: ws }), enabled: !!r, staleTime: 30_000 });
  // The job follows the run: refresh its operational view once when the run reaches a final state.
  useEffect(() => { if (r && !active) void qc.invalidateQueries({ queryKey: ["job-ops", ws, r.jobId] }); }, [r?.state]); // eslint-disable-line react-hooks/exhaustive-deps

  const registered = useQuery({ queryKey: ["run-versions", ws, id], queryFn: () => api.registry.listVersionsByRun({ workspaceId: ws, runId: id }), enabled: r?.state === "succeeded" });
  const versions = registered.data?.versions ?? [];
  const [registering, setRegistering] = useState<string | null>(null);
  const [renaming, setRenaming] = useState<string | null>(null);
  const [test, setTest] = useState<TestTarget | null>(null);
  const [evaluate, setEvaluate] = useState<EvalTarget | null>(null);
  const [confirm, setConfirm] = useState<"stop" | "delete" | null>(null);
  const refresh = () => { void qc.invalidateQueries({ queryKey: ["run", ws, id] }); void qc.invalidateQueries({ queryKey: ["runs", ws, r?.projectId] }); };
  const register = useMutation({
    mutationFn: (modelName: string) => api.registry.registerFromRun({ workspaceId: ws, runId: id, modelName }),
    onSuccess: () => { setRegistering(null); void qc.invalidateQueries({ queryKey: ["run-versions", ws, id] }); void qc.invalidateQueries({ queryKey: ["models", ws] }); },
  });
  const rename = useMutation({ mutationFn: (name: string) => api.tracking.updateRun({ workspaceId: ws, runId: id, name }), onSuccess: () => { setRenaming(null); refresh(); } });
  const stop = useMutation({ mutationFn: () => api.tracking.stopRun({ workspaceId: ws, runId: id }), onSuccess: () => { setConfirm(null); refresh(); } });
  const remove = useMutation({ mutationFn: () => api.tracking.deleteRun({ workspaceId: ws, runId: id }), onSuccess: () => { void qc.invalidateQueries({ queryKey: ["runs", ws, r?.projectId] }); onBack(); } });
  const resume = useMutation({ mutationFn: () => api.scheduler.resumeJob({ workspaceId: ws, jobId: r!.jobId }), onSuccess: refresh });
  const retrain = useMutation({
    mutationFn: () => retrainFrom(ws, r!),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["project-recipes", ws, r!.projectId] }); go(`projects/${r!.projectId}?tab=recipe`); },
  });
  const cook = useMutation({
    mutationFn: () => cookAgain(ws, r!),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["project-recipes", ws, r!.projectId] }); go(`projects/${r!.projectId}?tab=recipe`); },
  });

  if (!r) return <div className="p-6 text-sm text-fg-muted">{run.error ? <ErrorBox error={errorText(run.error)} onRetry={() => void run.refetch()} /> : t("Loading…")}</div>;

  const o = ops.data;
  const attempt = o?.attempts[o.attempts.length - 1];
  const lastMetric = o?.metrics[o.metrics.length - 1];
  const hostNames = (attempt?.hostIds ?? []).map((h) => map.data?.hosts.find((x) => x.id === h)?.name ?? h.slice(0, 8));
  const primary = metrics.find((m) => m.key === r.primaryMetricKey);
  const isCode = r.origin === "code";
  const codeLocal = isCode && !r.jobId;
  const codeBeatAge = r.heartbeatAt ? now - secs(r.heartbeatAt) : null;
  const lower = lowerIsBetter(r.primaryMetricKey);
  const beatAge = attempt?.lastHeartbeatAt ? now - secs(attempt.lastHeartbeatAt) : null;
  const failure = st === "failed" ? errorExcerpt(lines) : null;
  const fileList = files.data?.artifacts ?? [];
  const detail = trainDetail(r.progress);
  const waiting = active && !!r.stateReason && ["queued", "scheduling", "starting"].includes(st);

  return (
    <>
      <button className="mb-3 text-sm text-primary" onClick={onBack}>← {t("Project")}</button>
      <PageHeader
        title={<span className="inline-flex items-center gap-2"><span className="font-normal text-fg-subtle">#{r.number}</span>{r.name}
          <button className="rounded p-1 text-fg-subtle hover:bg-surface-2 hover:text-fg" aria-label={t("Rename")} title={t("Rename")} onClick={() => setRenaming(r.name)}><Pencil className="h-3.5 w-3.5" /></button></span>}
        subtitle={<span className="flex flex-wrap items-center gap-x-2 gap-y-1"><StateBadge state={st} />{isCode ? <span className="font-mono text-xs">{r.code?.script || t("Code")}</span> : <>{r.recipe} / {r.variant} · {t("task {task}", { task: r.task })}</>}
          {r.createdBy && <> · {r.createdBy}</>} · <RelTime at={r.createdAt} /></span>}
        actions={<>
          {active && <button className="btn-ghost" onClick={() => setConfirm("stop")}><Square className="h-4 w-4" />{t("Stop")}</button>}
          {!isCode && <button className="btn-ghost" disabled={cook.isPending} onClick={() => cook.mutate()}><CookingPot className="h-4 w-4" />{t("Cook again from #{n}", { n: r.number })}</button>}
          {!isCode && r.state === "succeeded" && r.revisionNumber > 0 && (
            <button className="btn-ghost" disabled={retrain.isPending} onClick={() => retrain.mutate()}>
              <RotateCw className="h-4 w-4" />{t("Train again from #{n}", { n: r.number })}</button>)}
          {r.state === "succeeded" && <button className={versions.length ? "btn-ghost" : "btn-primary"} onClick={() => setRegistering(r.name)}>{versions.length ? t("Register as another model") : t("Register model")}</button>}
          {!active && <button className="btn-ghost px-2 text-danger" aria-label={t("Delete run")} title={t("Delete run")} onClick={() => setConfirm("delete")}><Trash2 className="h-4 w-4" /></button>}
        </>} />
      <ErrorBox error={run.error ? errorText(run.error) : stop.error ? errorText(stop.error) : resume.error ? errorText(resume.error) : cook.error ? errorText(cook.error) : retrain.error ? errorText(retrain.error) : null} />

      {st === "failed" && codeLocal && (
        <div className="mb-4 rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">
          <div className="font-medium">{r.hasExitCode ? t("The process exited with code {n}", { n: r.exitCode }) : t("The process stopped sending heartbeats")}</div>
        </div>)}
      {st === "failed" && !codeLocal && (
        <div className="mb-4 rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">
          <div className="font-medium">{t("Training failed")}{r.attemptCount > 1 ? ` · ${t("after {n} attempts", { n: r.attemptCount })}` : ""}</div>
          {failure?.headline ? <div className="mono mt-1 break-words text-[13px] font-semibold">{failure.headline}</div>
            : <div className="mt-0.5">{reasonText(r.stateReason) || t("The process failed — open the log to see why")}</div>}
          {failure && failure.lines.length > 1 && <pre className="mono mt-2 max-h-56 overflow-auto whitespace-pre-wrap break-words rounded-md bg-[#0b0e13] p-2 text-[11px] leading-[1.45]" style={{ color: "#d5dae3" }}>{failure.lines.join("\n")}</pre>}
          <div className="mt-2 flex flex-wrap gap-2">
            <button className="btn-ghost h-8" onClick={() => setTab("log")}>{t("Open full log")}</button>
            {(o?.checkpoints.length ?? 0) > 0 && <button className="btn-ghost h-8" disabled={resume.isPending} onClick={() => resume.mutate()}><RotateCw className="h-4 w-4" />{t("Resume from checkpoint")}</button>}
            {!isCode && <button className="btn-ghost h-8" disabled={cook.isPending} onClick={() => cook.mutate()}><CookingPot className="h-4 w-4" />{t("Cook again from #{n}", { n: r.number })}</button>}
          </div>
        </div>)}
      {st === "cancelled" && <div className="mb-4 rounded-lg border border-line bg-surface-2 px-3 py-2 text-sm text-fg-muted">{reasonText(r.stateReason) || t("This run was stopped.")}</div>}
      {r.state === "succeeded" && versions.length === 0 && !registered.isLoading && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-primary/40 bg-primary/10 px-3 py-2 text-sm">
          <button className="btn-primary h-8" onClick={() => setRegistering(r.name)}>{t("Register model")}</button>
        </div>)}
      {versions.map((v) => (
        <div key={v.id} className="mb-4 flex flex-wrap items-center gap-3 rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-sm">
          <span>{t("Model")} <b>{v.modelName} v{v.version}</b> <StateBadge state={v.stage} /></span>
          <span className="ml-auto inline-flex gap-2">
            <button className="btn-primary h-8" onClick={() => setTest({ kind: "version", versionId: v.id, title: `${v.modelName} v${v.version}`, task: v.task })}><FlaskConical className="h-4 w-4" />{t("Test")}</button>
            <button className="btn-ghost h-8" onClick={() => setEvaluate({ versionId: v.id, title: `${v.modelName} v${v.version}`, task: v.task })}><Gauge className="h-4 w-4" />{t("Evaluate")}</button>
          </span>
        </div>))}

      {active && codeLocal && (
        <div className={`card mb-4 text-xs ${codeBeatAge !== null && codeBeatAge > 60 ? "text-danger" : "text-fg-subtle"}`}>
          {t("Heartbeat")} {codeBeatAge !== null ? t("{n}s ago", { n: Math.max(0, Math.round(codeBeatAge)) }) : "—"} · {t("running on {host}", { host: r.hostName || "—" })}
        </div>)}
      {active && !codeLocal && (
        <div className="card mb-4">
          <PhaseBar state={st} progress={r.progress} />
          {r.progress && (detail
            ? <div className="mt-3"><TrainingLive r={r} progress={r.progress} d={detail} metrics={metrics} checkpoints={o?.checkpoints ?? []} now={now} lower={lower} /></div>
            : <div className="mt-3"><ProgressLine progress={r.progress} /></div>)}
          {waiting && <div className="mt-3 flex items-start gap-2 text-sm text-warning"><Clock className="mt-0.5 h-4 w-4 shrink-0" /><span>{reasonText(r.stateReason)}</span></div>}
          <div className={`mt-3 text-xs ${beatAge !== null && beatAge > 120 ? "text-danger" : "text-fg-subtle"}`}>
            {attempt ? <>{t("Heartbeat")} {beatAge !== null ? t("{n}s ago", { n: Math.max(0, Math.round(beatAge)) }) : "—"}{beatAge !== null && beatAge > 120 ? ` · ${t("signal lost")}` : ""}
              {hostNames.length > 0 && <> · {t("running on {host}", { host: hostNames.join(", ") })}</>}
              {lastMetric && <> · CPU {lastMetric.cpuPercent.toFixed(0)}% · RAM {bytes(lastMetric.memoryBytes)}{lastMetric.gpuUtil > 0 ? ` · GPU ${lastMetric.gpuUtil.toFixed(0)}% · VRAM ${bytes(lastMetric.vramBytes)}` : ""}</>}</>
              : t("Waiting for a machine — nothing is running yet.")}
          </div>
        </div>)}

      <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Tile label={r.primaryMetricKey || t("Result")} value={r.hasPrimaryMetric ? metricText(r.primaryMetricValue) : "—"}
          sub={primary ? t("best {v} · {n} evaluations", { v: metricText(lower ? primary.min : primary.max), n: Number(primary.points) }) : undefined} />
        <Tile label={t("Duration")} value={dur(runSeconds(r, now))}
          sub={r.startedAt ? t("started {at}", { at: dateTime(r.startedAt) }) : t("not started yet")} />
        {isCode ? <Tile label={t("Code")} value={<span className="font-mono text-base">{r.code?.commit ? r.code.commit.slice(0, 8) : "—"}</span>}
          sub={[r.code?.branch, r.code?.dirty ? t("uncommitted changes") : ""].filter(Boolean).join(" · ") || undefined} />
        : <Tile label={t("Data")} value={r.datasetName ? <button className="truncate text-left hover:text-primary" onClick={() => go(`datasets/${r.datasetId}`)}>{r.datasetName} <span className="text-fg-subtle">v{r.datasetVersionNumber}</span></button> : "—"}
          sub={backboneOnly(r) ? t("trained from scratch") : t("fine-tuned from pretrained weights")} />}
        {codeLocal ? <Tile label={t("Machine")} value={r.hostName || "—"} sub={r.code?.python ? `Python ${r.code.python}` : undefined} />
        : <Tile label={t("Machine")} value={`${r.gpus} GPU · ${r.cpus} CPU`}
          sub={[hostNames.join(", "), r.attemptCount > 1 ? t("{n} attempts", { n: r.attemptCount }) : ""].filter(Boolean).join(" · ") || undefined} />}
      </div>

      <Tabs<Tab> value={tab} onChange={setTab} tabs={[
        { key: "overview", label: t("Overview") }, { key: "metrics", label: t("Metrics"), count: metrics.length },
        ...(codeLocal ? [] : [{ key: "log" as Tab, label: t("Log") }]),
        { key: "config", label: t("Configuration") }, { key: "files", label: t("Files"), count: files.data ? fileList.length : undefined }]} />

      {tab === "overview" && <Overview ws={ws} r={r} metrics={metrics} active={active} lines={lines} events={o?.events ?? []} attempts={o?.attempts ?? []}
        onLog={() => setTab("log")} onMetrics={() => setTab("metrics")} onJob={() => go(`jobs/${r.jobId}`)} />}
      {tab === "metrics" && <MetricsTab ws={ws} r={r} metrics={metrics} active={active} loading={summary.isLoading} />}
      {tab === "log" && <LogPanel lines={lines} height="65vh" name={`run-${r.number}-${r.name}`}
        onLoadEarlier={lines.length >= logTail && logTail < 20_000 ? () => setLogTail(Math.min(20_000, logTail * 5)) : undefined} />}
      {tab === "config" && <ConfigTab ws={ws} r={r} go={go} />}
      {tab === "files" && (active ? <EmptyState title={t("No files yet")} />
        : fileList.length === 0 ? <EmptyState title={t("No files")}>{files.error ? errorText(files.error) : null}</EmptyState> : (
          <div className="card overflow-x-auto p-0">
            <table className="w-full text-sm">
              <thead className="border-b border-line text-left text-xs text-fg-muted"><tr><th className="px-4 py-2 font-medium">{t("File")}</th><th className="px-4 py-2 text-right font-medium">{t("Size")}</th><th className="w-10" /></tr></thead>
              <tbody>{fileList.map((f) => (
                <tr key={f.path} className="border-b border-line last:border-0">
                  <td className="px-4 py-2 font-mono text-xs">{f.path}</td><td className="whitespace-nowrap px-4 py-2 text-right text-xs tabular-nums text-fg-muted">{bytes(f.sizeBytes)}</td>
                  <td className="px-2"><a className="btn-ghost h-8 px-2" href={f.url} download={f.path.split("/").pop()} aria-label={t("Download {f}", { f: f.path })}><Download className="h-4 w-4" /></a></td>
                </tr>))}</tbody>
              <tfoot><tr><td className="px-4 py-2 text-xs text-fg-muted">{t("{n} files", { n: fileList.length })}</td>
                <td className="px-4 py-2 text-right text-xs tabular-nums text-fg-muted">{bytes(fileList.reduce((s, f) => s + Number(f.sizeBytes), 0))}</td><td /></tr></tfoot>
            </table>
          </div>))}

      {test && <ModelTestSheet ws={ws} target={test} onClose={() => setTest(null)} />}
      {evaluate && <EvaluateSheet ws={ws} target={evaluate} onClose={() => setEvaluate(null)} />}
      {confirm === "stop" && <StopRunSheet run={r} busy={stop.isPending} onClose={() => setConfirm(null)} onConfirm={() => stop.mutate()} />}
      {confirm === "delete" && (
        <ConfirmDialog title={t("Delete run #{n}?", { n: r.number })} label="Delete run" busy={remove.isPending} onClose={() => setConfirm(null)} onConfirm={() => remove.mutate()}
          consequence={<>{t("The run, its metrics and its files are removed for good. Models already registered from it must be deleted first.")}
            {remove.error && <div className="mt-3"><ErrorBox error={errorText(remove.error)} /></div>}</>} />)}
      {renaming !== null && (
        <Dialog title={t("Rename run")} onClose={() => setRenaming(null)}>
          <div className="space-y-3">
            <Field label={t("Run name")}><input className="input" value={renaming} maxLength={80} autoFocus onChange={(e) => setRenaming(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter" && renaming.trim()) rename.mutate(renaming.trim()); }} /></Field>
            <ErrorBox error={rename.error ? errorText(rename.error) : null} />
            <div className="flex justify-end gap-2">
              <button className="btn-ghost" onClick={() => setRenaming(null)}>{t("Cancel")}</button>
              <button className="btn-primary" disabled={!renaming.trim() || rename.isPending} onClick={() => rename.mutate(renaming.trim())}>{t("Save")}</button>
            </div>
          </div>
        </Dialog>)}
      {registering !== null && (
        <Dialog title={t("Register model")} onClose={() => setRegistering(null)}>
          <div className="space-y-3">
            <Field label={t("Model name")}>
              <input className="input" value={registering} onChange={(e) => setRegistering(e.target.value)} autoFocus /></Field>
            <ErrorBox error={register.error ? errorText(register.error) : null} />
            <div className="flex justify-end gap-2">
              <button className="btn-ghost" onClick={() => setRegistering(null)}>{t("Cancel")}</button>
              <button className="btn-primary" disabled={!registering.trim() || register.isPending} onClick={() => register.mutate(registering.trim())}>{t("Register model")}</button>
            </div>
          </div>
        </Dialog>)}
    </>
  );
}

function Tile({ label, value, sub }: { label: string; value: ReactNode; sub?: string }) {
  return (
    <div className="card min-w-0">
      <div className="truncate text-xs font-medium text-fg-muted">{label}</div>
      <div className="mt-1 truncate text-xl font-semibold tabular-nums">{value}</div>
      {sub && <div className="mt-0.5 truncate text-xs text-fg-subtle" title={sub}>{sub}</div>}
    </div>
  );
}

function useSeries(ws: string, runId: string, keys: string[], active: boolean, points = 500) {
  return useQuery({
    queryKey: ["metrics", ws, runId, keys.join("|"), points], enabled: keys.length > 0, refetchInterval: active ? 8000 : false, placeholderData: (old) => old,
    queryFn: () => api.tracking.getMetrics({ workspaceId: ws, runId, keys, maxPoints: points }),
  });
}

function Overview({ ws, r, metrics, active, lines, events, attempts, onLog, onMetrics, onJob }: {
  ws: string; r: Run; metrics: MetricInfo[]; active: boolean; lines: ReturnType<typeof useJobLog>; events: Parameters<typeof EventList>[0]["events"];
  attempts: { id: string; number: number; state: string; exitReason: string; exitCode: number; resumedFrom: string }[]; onLog: () => void; onMetrics: () => void; onJob: () => void;
}) {
  const hasPrimary = metrics.some((m) => m.key === r.primaryMetricKey);
  const series = useSeries(ws, r.id, hasPrimary ? [r.primaryMetricKey] : [], active, 800);
  const points = (series.data?.series[0]?.points ?? []).map((p) => [Number(p.step), p.value] as [number, number]);
  // Results first (validation / test), then the main training signals; the long tail lives in the Metrics tab.
  // Standard names are lowercase by convention (val/…, train/…); names in other casing are the recipe's own raw series.
  const rank = (k: string) => (k === r.primaryMetricKey ? 0 : /^(val|test|eval)\//.test(k) ? 1 : k === "train/loss" ? 2 : /^(train|lr)\//.test(k) ? 3 : 4);
  const key = [...metrics].sort((a, b) => rank(a.key) - rank(b.key) || a.key.localeCompare(b.key)).filter((m) => r.origin === "code" || rank(m.key) < 4).slice(0, 8);
  const tail = lines.slice(-12);
  const isCode = r.origin === "code" && !r.jobId;
  return (
    <div className="grid gap-4 xl:grid-cols-3">
      <div className="space-y-4 xl:col-span-2">
        {points.length > 0 ? <MetricChart title={r.primaryMetricKey} subtitle={t("last {v}", { v: metricText(points[points.length - 1][1]) })} series={[{ name: r.primaryMetricKey, points }]} height={260} />
          : !active && <div className="card text-sm text-fg-muted">{t("This run recorded no {k}.", { k: r.primaryMetricKey || t("result") })}</div>}
        {!isCode && (active || runState(r) === "failed") && (
          <div className="card">
            <div className="mb-2 flex items-center justify-between"><div className="text-sm font-medium">{t("Latest log lines")}</div>
              <button className="inline-flex items-center text-xs text-primary" onClick={onLog}>{t("Open full log")}<ChevronRight className="h-3.5 w-3.5" /></button></div>
            <pre className="mono max-h-56 overflow-auto whitespace-pre-wrap break-words rounded-md bg-[#0b0e13] p-3 text-[11px] leading-[1.45]" style={{ color: "#d5dae3" }}>
              {tail.length === 0 ? t("No log lines yet.") : tail.map((l) => l.text).join("\n")}</pre>
          </div>)}
        {!isCode && <div className="card">
          <div className="mb-2 flex items-center justify-between"><div className="text-sm font-medium">{t("Timeline")}</div>
            <button className="inline-flex items-center text-xs text-primary" onClick={onJob}>{t("Open job page")}<ChevronRight className="h-3.5 w-3.5" /></button></div>
          <EventList events={events} limit={10} />
        </div>}
      </div>
      <div className="space-y-4">
        <div className="card">
          <div className="mb-2 flex items-center justify-between"><div className="text-sm font-medium">{t("Key metrics")}</div>
            {metrics.length > key.length && <button className="inline-flex items-center text-xs text-primary" onClick={onMetrics}>{t("All {n}", { n: metrics.length })}<ChevronRight className="h-3.5 w-3.5" /></button>}</div>
          {key.length === 0 ? <div className="text-xs text-fg-subtle">{t("No metrics yet.")}</div> : (
            <table className="w-full text-sm">
              <thead className="text-left text-xs text-fg-subtle"><tr><th className="pb-1 font-medium">{t("Metric")}</th><th className="pb-1 text-right font-medium">{t("Last")}</th><th className="pb-1 text-right font-medium">{t("Best")}</th></tr></thead>
              <tbody>{key.map((m) => (
                <tr key={m.key} className="border-t border-line">
                  <td className="break-all py-1.5 pr-2 font-mono text-xs">{m.key}</td>
                  <td className="py-1.5 text-right tabular-nums">{metricText(m.last)}</td>
                  <td className="py-1.5 text-right tabular-nums text-fg-muted">{metricText(lowerIsBetter(m.key) ? m.min : m.max)}</td>
                </tr>))}</tbody>
            </table>)}
        </div>
        {attempts.length > 1 && (
          <div className="card">
            <div className="mb-2 text-sm font-medium">{t("Attempts")}</div>
            <table className="w-full text-xs">
              <tbody>{attempts.map((a) => (
                <tr key={a.id} className="border-t border-line first:border-0"><td className="py-1.5 pr-2 tabular-nums text-fg-subtle">#{a.number}</td><td className="pr-2"><StateBadge state={a.state} /></td>
                  <td className="text-fg-muted">{t(a.exitReason)}{a.exitCode ? ` (${t("code")} ${a.exitCode})` : ""}{a.resumedFrom ? ` · ${t("resumed from {x}", { x: a.resumedFrom })}` : ""}</td></tr>))}</tbody>
            </table>
          </div>)}
      </div>
    </div>
  );
}

/** Every metric the recipe wrote, grouped by prefix. Only open groups load their points, so a run with 60 loss terms stays fast. */
function MetricsTab({ ws, r, metrics, active, loading }: { ws: string; r: Run; metrics: MetricInfo[]; active: boolean; loading: boolean }) {
  const [q, setQ] = useState("");
  const [smoothing, setSmoothing] = useSticky("run:smoothing", 0);
  const [openList, setOpenList] = useSticky<string[] | null>("run:metric-groups", null);
  const groups = useMemo(() => {
    const needle = q.trim().toLowerCase();
    const by = new Map<string, MetricInfo[]>();
    for (const m of metrics) { if (needle && !m.key.toLowerCase().includes(needle)) continue; const g = groupOf(m.key); by.set(g, [...(by.get(g) ?? []), m]); }
    const primaryGroup = groupOf(r.primaryMetricKey);
    const order = (g: string) => (g === primaryGroup ? 0 : /^(val|test|eval)$/.test(g) ? 1 : g === "train" ? 2 : g === "lr" ? 3 : 4);
    return [...by.entries()].sort((a, b) => order(a[0]) - order(b[0]) || a[0].localeCompare(b[0]));
  }, [metrics, q, r.primaryMetricKey]);
  const isOpen = (g: string, i: number) => (q.trim() ? true : openList ? openList.includes(g) : i < 2);
  const toggle = (g: string) => setOpenList((old) => { const cur = old ?? groups.slice(0, 2).map(([n]) => n); return cur.includes(g) ? cur.filter((x) => x !== g) : [...cur, g]; });

  if (metrics.length === 0) return <EmptyState title={loading ? t("Loading…") : t("No metrics yet")} />;
  return (
    <>
      <div className="mb-3 flex flex-wrap items-center gap-3">
        <div className="relative">
          <Search className="pointer-events-none absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-fg-subtle" />
          <input className="input h-8 w-64 pl-7" placeholder={t("Find a metric…")} value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <label className="flex items-center gap-2 text-xs text-fg-muted">{t("Smoothing")}
          <input type="range" min={0} max={0.95} step={0.05} value={smoothing} onChange={(e) => setSmoothing(Number(e.target.value))} className="w-32" />
          <span className="w-8 tabular-nums">{smoothing.toFixed(2)}</span></label>
        <span className="ml-auto text-xs text-fg-subtle">{t("{n} metrics in {g} groups", { n: metrics.length, g: groups.length })}</span>
      </div>
      {groups.length === 0 && <div className="card py-10 text-center text-sm text-fg-subtle">{t("No metric matches “{q}”.", { q })}</div>}
      <div className="space-y-3">
        {groups.map(([g, items], i) => (
          <MetricGroup key={g} ws={ws} runId={r.id} name={g} items={items} open={isOpen(g, i)} onToggle={() => toggle(g)} active={active} smoothing={smoothing} />))}
      </div>
    </>
  );
}

function MetricGroup({ ws, runId, name, items, open, onToggle, active, smoothing }: {
  ws: string; runId: string; name: string; items: MetricInfo[]; open: boolean; onToggle: () => void; active: boolean; smoothing: number;
}) {
  const series = useSeries(ws, runId, open ? items.map((m) => m.key) : [], active, 400);
  const byKey = new Map((series.data?.series ?? []).map((s) => [s.key, s.points.map((p) => [Number(p.step), p.value] as [number, number])]));
  return (
    <div className="card p-0">
      <button className="flex w-full items-center gap-2 px-4 py-2.5 text-left" onClick={onToggle} aria-expanded={open}>
        <ChevronRight className={`h-4 w-4 text-fg-subtle transition-transform ${open ? "rotate-90" : ""}`} />
        <span className="text-sm font-medium">{name}</span><span className="rounded-full bg-surface-2 px-1.5 text-xs text-fg-muted">{items.length}</span>
        {!open && <span className="ml-2 truncate text-xs text-fg-subtle">{items.slice(0, 4).map((m) => `${m.key.slice(m.key.indexOf("/") + 1)} ${metricText(m.last)}`).join(" · ")}</span>}
      </button>
      {open && (
        <div className="grid gap-x-6 gap-y-5 border-t border-line p-4 md:grid-cols-2 2xl:grid-cols-3">
          {items.map((m) => {
            const pts = byKey.get(m.key);
            return pts ? <MetricChart key={m.key} bare title={m.key} subtitle={metricText(m.last)} series={[{ name: m.key, points: pts }]} height={170} smoothing={smoothing} />
              : <div key={m.key}><div className="mb-1 truncate text-sm font-medium">{m.key}</div><div className="skeleton" style={{ height: 170 }} /></div>;
          })}
        </div>)}
    </div>
  );
}

/** What exactly was trained: every parameter with its effective value (overrides marked), and where the run came from. */
function ConfigTab({ ws, r, go }: { ws: string; r: Run; go: (path: string) => void }) {
  const isCode = r.origin === "code";
  const recipe = useQuery({ queryKey: ["recipe", ws, r.recipe], enabled: !isCode, queryFn: () => api.catalog.getRecipe({ workspaceId: ws, name: r.recipe }) });
  const defs = useMemo(() => { try { return (JSON.parse(recipe.data?.manifestJson || "{}").params ?? {}) as Record<string, ParamDef>; } catch { return {}; } }, [recipe.data]);
  const given = useMemo(() => { try { return JSON.parse(r.paramsJson || "{}") as Record<string, unknown>; } catch { return {}; } }, [r.paramsJson]);
  const keys = [...new Set([...Object.keys(defs), ...Object.keys(given)])];
  const [copied, setCopied] = useState(false);
  const codeRows: [string, ReactNode][] = [
    [t("Repository"), <span key="g" className="font-mono text-xs">{r.code?.gitRemote || "—"}</span>],
    [t("Branch"), r.code?.branch || "—"],
    [t("Commit"), <span key="c" className="font-mono text-xs">{r.code?.commit ? `${r.code.commit.slice(0, 12)}${r.code.dirty ? " + " + t("uncommitted changes") : ""}` : "—"}</span>],
    [t("Script"), <span key="s" className="font-mono text-xs">{[r.code?.script, ...(r.code?.argv ?? [])].filter(Boolean).join(" ") || "—"}</span>],
    [t("Machine"), r.hostName || "—"],
    ["Python", r.code?.python || "—"],
    [t("Exit code"), r.hasExitCode ? String(r.exitCode) : "—"],
    [t("Job"), r.jobId ? <button key="j" className="text-primary" onClick={() => go(`jobs/${r.jobId}`)}>{t("Open job page")}</button> : "—"],
    [t("Created"), `${dateTime(r.createdAt)}${r.createdBy ? ` · ${r.createdBy}` : ""}`],
    [t("Ended"), dateTime(r.endedAt)],
    [t("Run ID"), <ShortId key="i" id={r.id} />],
  ];
  const rows: [string, ReactNode][] = isCode ? codeRows : [
    [t("Recipe"), <button key="r" className="text-primary" onClick={() => go(`catalog/${r.recipe}`)}>{recipe.data?.displayName ?? r.recipe}</button>],
    [t("Variant"), r.variant],
    [t("Revision"), r.revisionNumber > 0 ? <span key="v" className="font-mono">r{r.revisionNumber}</span> : <span key="v" className="text-fg-muted">{t("started before revisions existed")}</span>],
    [t("Train kind"), runKind(r)],
    [t("Starting point"), backboneOnly(r) ? t("Pretrained backbone only") : t("Variant's pretrained weights")],
    [t("Dataset"), r.datasetName ? <button key="d" className="text-primary" onClick={() => go(`datasets/${r.datasetId}`)}>{r.datasetName} v{r.datasetVersionNumber}</button> : "—"],
    [t("Resources"), `${r.gpus} GPU · ${r.cpus} CPU`],
    [t("Created"), `${dateTime(r.createdAt)}${r.createdBy ? ` · ${r.createdBy}` : ""}`],
    [t("Started"), dateTime(r.startedAt)],
    [t("Ended"), dateTime(r.endedAt)],
    [t("Run ID"), <ShortId key="i" id={r.id} />],
    [t("Job"), r.jobId ? <button key="j" className="text-primary" onClick={() => go(`jobs/${r.jobId}`)}>{t("Open job page")}</button> : "—"],
  ];
  return (
    <div className="grid gap-4 xl:grid-cols-3">
      <div className="card p-0 xl:col-span-2">
        <div className="flex items-center justify-between border-b border-line px-4 py-2.5">
          <div className="text-sm font-medium">{t("Parameters")}</div>
          <button className="btn-ghost h-8 px-2 text-xs" onClick={() => { void navigator.clipboard.writeText(JSON.stringify(given, null, 2)); setCopied(true); setTimeout(() => setCopied(false), 1200); }}>
            <Copy className="h-3.5 w-3.5" />{copied ? t("Copied") : t("Copy JSON")}</button>
        </div>
        {keys.length === 0 ? <div className="p-4 text-sm text-fg-muted">{isCode ? t("No parameters") : t("This recipe has no parameters.")}</div> : (
          <table className="w-full text-sm">
            <thead className="border-b border-line text-left text-xs text-fg-muted"><tr><th className="px-4 py-2 font-medium">{t("Name")}</th><th className="px-4 py-2 font-medium">{t("Value")}</th><th className="px-4 py-2 font-medium">{t("Default")}</th></tr></thead>
            <tbody>{keys.map((k) => {
              const d = defs[k] ?? {}, set = given[k] !== undefined, changed = set && d.default !== undefined && String(given[k]) !== String(d.default);
              return (
                <tr key={k} className="border-b border-line last:border-0 align-top">
                  <td className="whitespace-nowrap px-4 py-2 font-mono text-xs">{k}</td>
                  <td className={`whitespace-nowrap px-4 py-2 tabular-nums ${changed ? "font-semibold text-primary" : ""}`}>{String(set ? given[k] : d.default ?? "—")}</td>
                  <td className="whitespace-nowrap px-4 py-2 tabular-nums text-fg-muted">{d.from ? t("from the dataset") : String(d.default ?? "—")}</td>
                </tr>);
            })}</tbody>
          </table>)}
      </div>
      <div className="card">
        <div className="mb-2 text-sm font-medium">{t("Setup")}</div>
        <dl className="space-y-2 text-sm">
          {rows.map(([k, v]) => <div key={k} className="flex items-start justify-between gap-4"><dt className="shrink-0 text-fg-muted">{k}</dt><dd className="min-w-0 truncate text-right">{v}</dd></div>)}
        </dl>
      </div>
    </div>
  );
}
