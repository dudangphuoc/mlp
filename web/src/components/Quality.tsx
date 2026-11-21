import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BrainCircuit, Gauge, Lightbulb, Loader2, Target, Users } from "lucide-react";
import { api, errorReason, errorText } from "../lib/api";
import type { QualitySummary } from "../gen/mlplatform/v1/labeling_pb";
import { EmptyState, ErrorBox, Skeleton } from "./ui";
import { RunSheet } from "./AiActivity";
import { ExternalBadge } from "./Assist";
import { ExternalConfirm } from "./ExternalConfirm";
import { translate as t, useT } from "../i18n";

/*
 * Hermes phase 4 — quality per annotator and calibration of Hermes against people. Every number is a count of real submits,
 * reviews and checks; the recommendation appears only with enough reviewed evidence, and the AI summary shows exactly what was sent.
 */

const pct = (n: number, d: number) => (d > 0 ? `${Math.round((100 * n) / d)}%` : "—");

export function QualityProjectTab({ ws, projectId }: { ws: string; projectId: string }) {
  const qc = useQueryClient();
  const tt = useT();
  const q = useQuery({ queryKey: ["quality", ws, projectId], queryFn: () => api.labeling.getQualityReport({ workspaceId: ws, projectId }), refetchInterval: 15_000 });
  const stats = useQuery({ queryKey: ["hermes-stats", ws, projectId], queryFn: () => api.labeling.getHermesStats({ workspaceId: ws, projectId }) });
  const [summary, setSummary] = useState<QualitySummary | null>(null);
  const [confirm, setConfirm] = useState<string | null>(null);
  const [run, setRun] = useState<string | null>(null);
  const [showSent, setShowSent] = useState(false);
  const summarize = useMutation({
    mutationFn: (ok: boolean) => api.labeling.summarizeQuality({ workspaceId: ws, projectId, lang: tt.lang, confirmExternal: ok }),
    onSuccess: (s) => { setConfirm(null); setSummary(s); },
    onError: (e) => { if (errorReason(e) === "LABEL_EXTERNAL_CONFIRM") setConfirm(errorText(e)); },
  });
  const apply = useMutation({
    mutationFn: (threshold: number) => api.labeling.setHermesSettings({ workspaceId: ws, projectId, enabled: true, autoAcceptThreshold: threshold }),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["quality", ws, projectId] }); void qc.invalidateQueries({ queryKey: ["hermes-stats", ws, projectId] }); },
  });
  if (q.isLoading) return <Skeleton rows={6} />;
  const r = q.data;
  if (!r) return <ErrorBox error={q.error ? errorText(q.error) : null} onRetry={() => void q.refetch()} />;
  const c = r.calibration!;
  const maxReviewed = Math.max(1, ...c.buckets.map((b) => b.reviewed));
  return (
    <div className="space-y-4">
      <ErrorBox error={apply.error ? errorText(apply.error) : summarize.error && errorReason(summarize.error) !== "LABEL_EXTERNAL_CONFIRM" ? errorText(summarize.error) : null} />

      <section className="card p-0">
        <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
          <h2 className="flex items-center gap-2 text-sm font-semibold"><Users className="h-4 w-4 text-primary" />{t("Annotators")}</h2>
        </div>
        {r.annotators.length === 0 ? <div className="px-4 pb-4 text-sm text-fg-muted">{t("No item has been assigned yet.")}</div> : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead><tr className="border-y border-line text-left text-xs text-fg-subtle">
                <th className="px-4 py-2">{t("Annotator")}</th><th>{t("Submitted")}</th><th>{t("Accepted")}</th>
                <th>{t("Sent back")}</th>
                <th>{t("Hermes pass / flag / fail")}</th><th>{t("Auto-accepted")}</th><th>{t("Undone")}</th><th className="pr-4">{t("Avg. confidence")}</th>
              </tr></thead>
              <tbody>
                {r.annotators.map((a) => {
                  const sub = Number(a.submitted);
                  return (
                    <tr key={a.annotatorId} className="border-b border-line last:border-0">
                      <td className="px-4 py-2 font-medium">{a.annotatorName}<span className="block text-xs font-normal text-fg-subtle">{t("{n} items", { n: String(a.items) })}</span></td>
                      <td className="tabular-nums">{String(a.submitted)}</td>
                      <td className="tabular-nums">{String(a.accepted)} <span className="text-xs text-fg-subtle">({pct(Number(a.accepted), sub)})</span></td>
                      <td className={`tabular-nums ${Number(a.returnedEver) > 0 ? "text-warning" : ""}`}>{String(a.returnedEver)} <span className="text-xs text-fg-subtle">({pct(Number(a.returnedEver), sub)})</span></td>
                      <td className="tabular-nums"><span className="text-success">{String(a.hermesPass)}</span> / <span className="text-warning">{String(a.hermesFlag)}</span> / <span className="text-danger">{String(a.hermesFail)}</span></td>
                      <td className="tabular-nums">{String(a.autoAccepted)}</td>
                      <td className="tabular-nums">{String(a.undone)}</td>
                      <td className="pr-4 tabular-nums">{a.avgConfidence >= 0 ? `${Math.round(a.avgConfidence * 100)}%` : "—"}</td>
                    </tr>);
                })}
              </tbody>
            </table>
          </div>)}
      </section>

      <div className="grid gap-4 xl:grid-cols-2">
        <section className="card">
          <h2 className="mb-1 flex items-center gap-2 text-sm font-semibold"><Gauge className="h-4 w-4 text-primary" />{t("Is Hermes right? (calibration)")}</h2>
          <div className="mb-3 grid grid-cols-2 gap-2 text-sm sm:grid-cols-4">
            <Num label={t("Checks")} v={c.checks} />
            <Num label={t("Decided by a person")} v={c.reviewed} />
            <Num label={t("Pass, person disagreed")} v={c.passDisagreed} sub={pct(c.passDisagreed, c.passReviewed)} tone={c.passDisagreed > 0 ? "text-danger" : ""} />
            <Num label={t("Fail/flag, person agreed")} v={c.failAgreed} sub={pct(c.failAgreed, c.failReviewed)} />
          </div>
          {c.autoUnreviewed > 0 && <p className="mb-3 text-xs text-warning">{t("{n} auto-accepted item(s) were never looked at by a person: they do not count as evidence.", { n: c.autoUnreviewed })}</p>}
          <div className="text-xs font-medium text-fg-muted">{t("Passing checks by model confidence — share a person accepted")}</div>
          <div className="mt-2 space-y-1.5">
            {c.buckets.map((b) => (
              <div key={b.lo} className="flex items-center gap-2 text-xs">
                <span className="w-20 tabular-nums text-fg-muted">{Math.round(b.lo * 100)}–{Math.round(b.hi * 100)}%</span>
                <span className="relative h-3 flex-1 overflow-hidden rounded bg-surface-2">
                  <span className="absolute inset-y-0 left-0 bg-primary/25" style={{ width: `${(100 * b.reviewed) / maxReviewed}%` }} />
                  <span className="absolute inset-y-0 left-0 bg-success" style={{ width: `${(100 * b.humanAgreed) / maxReviewed}%` }} />
                </span>
                <span className="w-28 text-right tabular-nums">{b.reviewed === 0 ? t("no data") : `${b.humanAgreed}/${b.reviewed} · ${pct(b.humanAgreed, b.reviewed)}`}</span>
              </div>))}
          </div>
        </section>

        <section className="card">
          <h2 className="mb-1 flex items-center gap-2 text-sm font-semibold"><Target className="h-4 w-4 text-primary" />{t("Auto-accept threshold")}</h2>
          <table className="mb-3 w-full text-xs">
            <thead><tr className="text-left text-fg-subtle"><th className="py-1">{t("Threshold")}</th><th>{t("Would auto-accept")}</th><th>{t("Of those, reviewed")}</th><th>{t("Person disagreed")}</th></tr></thead>
            <tbody>
              {c.options.map((o) => (
                <tr key={o.threshold} className={`border-t border-line ${Math.abs(o.threshold - c.currentThreshold) < 1e-6 ? "bg-primary/5 font-medium" : ""} ${Math.abs(o.threshold - c.recommended) < 1e-6 ? "text-success" : ""}`}>
                  <td className="py-1 tabular-nums">≥ {Math.round(o.threshold * 100)}%{Math.abs(o.threshold - c.currentThreshold) < 1e-6 ? ` · ${t("current")}` : ""}</td>
                  <td className="tabular-nums">{o.wouldAutoAccept}</td><td className="tabular-nums">{o.reviewed}</td>
                  <td className="tabular-nums">{o.errorRate < 0 ? "—" : `${o.disagreed} (${(o.errorRate * 100).toFixed(1)}%)`}</td>
                </tr>))}
            </tbody>
          </table>
          <div className={`rounded-md border px-3 py-2 text-sm ${c.recommended >= 0 ? "border-success/30 bg-success/10" : "border-line bg-surface-2"}`}>
            {c.recommended >= 0 ? <b>{t("Recommended: ≥ {p}", { p: `${Math.round(c.recommended * 100)}%` })}. </b> : null}{t(c.recommendation)}
          </div>
          {r.canManage && c.recommended >= 0 && Math.abs(c.recommended - c.currentThreshold) > 1e-6 && (
            <button className="btn-primary mt-3" disabled={apply.isPending} onClick={() => apply.mutate(c.recommended)}>
              {t("Use ≥ {p} for auto-accept", { p: `${Math.round(c.recommended * 100)}%` })}</button>)}
          {stats.data && !stats.data.project?.hermesEnabled && <p className="mt-2 text-xs text-fg-subtle">{t("Automatic checks are off: turn them on in the Hermes tab for the threshold to apply.")}</p>}
        </section>
      </div>

      <section className="card">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="flex items-center gap-2 text-sm font-semibold"><Lightbulb className="h-4 w-4 text-primary" />{t("AI summary for the Lead")}</h2>
            <p className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-fg-subtle">{t("Analyst model")}: {r.analystModel || t("none")}{r.analystExternal && <ExternalBadge />}</p>
          </div>
          {r.canManage && <button className="btn-ghost" disabled={summarize.isPending || r.analystState === "missing"} onClick={() => summarize.mutate(false)}>
            {summarize.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <BrainCircuit className="h-4 w-4" />}{summary ? t("Write again") : t("Write summary")}</button>}
        </div>
        {r.analystState === "missing" && <p className="mt-2 text-xs text-warning">{t("No analyst model is ready — choose one on the AI models page.")}</p>}
        {summary && (
          <div className="mt-3 space-y-2">
            <div className="whitespace-pre-wrap rounded-lg border border-line bg-surface-2 p-3 text-sm">{summary.text}</div>
            <div className="flex flex-wrap items-center gap-3 text-xs text-fg-subtle">
              <span>{summary.model}</span>{summary.external && <ExternalBadge />}
              <span>{Object.entries(summary.legend).map(([k, v]) => `${k} = ${v}`).join(" · ")}</span>
              <button className="underline" onClick={() => setRun(summary.aiRunId)}>{t("Open the AI call")}</button>
              <button className="underline" onClick={() => setShowSent(!showSent)}>{showSent ? t("Hide data sent") : t("Show data sent")}</button>
            </div>
            {showSent && <pre className="mono max-h-72 overflow-auto rounded-lg border border-line bg-[#0b0e13] p-3 text-[11px]" style={{ color: "#e6e9ef" }}>{summary.sentJson}</pre>}
          </div>)}
        {!summary && !r.canManage && <EmptyState bare title={t("Only a Lead can generate the AI summary")} />}
      </section>
      {confirm && <ExternalConfirm message={confirm} busy={summarize.isPending} onClose={() => setConfirm(null)} onConfirm={() => summarize.mutate(true)}
        what={t("Only the aggregated numbers above are sent (annotators as A1, A2…), plus the project name.")} />}
      {run && <RunSheet id={run} onClose={() => setRun(null)} />}
    </div>
  );
}

function Num({ label, v, sub, tone }: { label: string; v: number; sub?: string; tone?: string }) {
  return (
    <div className="rounded-lg border border-line px-3 py-2">
      <div className="text-xs text-fg-muted">{label}</div>
      <div className={`text-lg font-semibold tabular-nums ${tone ?? ""}`}>{v}{sub && <span className="ml-1 text-xs font-normal text-fg-subtle">{sub}</span>}</div>
    </div>
  );
}
