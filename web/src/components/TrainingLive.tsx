import type { ReactNode } from "react";
import { CheckCircle2, CircleDashed, HardDriveDownload, RotateCw, Target, TriangleAlert } from "lucide-react";
import { dur, secs } from "./JobProgress";
import { metricText } from "./MetricChart";
import { translate as t } from "../i18n";
import type { CheckpointInfo, JobProgress } from "../gen/mlplatform/v1/scheduler_pb";
import type { MetricInfo, Run } from "../gen/mlplatform/v1/tracking_pb";

/** Training detail the shim reads from the recipe's own output lines (round 1 R24). Epochs are 0-based: epoch 3 of 24 is the 4th. */
export type TrainDetail = {
  epoch: number; epochs: number; step: number; steps: number; sub: "train" | "validate" | ""; completed_epochs: number;
  loss?: number | "nan" | "inf"; val_step?: number; val_steps?: number; epoch_seconds?: number;
  stages?: { key: string; label: string; until: number | null }[];
};

export function trainDetail(p?: JobProgress): TrainDetail | null {
  if (!p?.detailJson || p.phase !== "running") return null;
  try {
    const d = JSON.parse(p.detailJson) as TrainDetail;
    return d.epochs > 0 && d.steps > 0 && d.epoch >= 0 ? d : null;
  } catch { return null; }
}

const VAL_KEYS = ["val/precision", "val/recall", "val/f1"];

/**
 * S-09-3 (round 1 §8.5): the training loop of the canvas, live — where it is (epoch, step, training or validating), how long is left, which
 * stage, the latest loss, the validation numbers so far and the latest checkpoint. Every number says where it comes from.
 */
export function TrainingLive({ r, progress, d, metrics, checkpoints, now, lower }: {
  r: Run; lower: boolean; progress: JobProgress; d: TrainDetail; metrics: MetricInfo[]; checkpoints: CheckpointInfo[]; now: number;
}) {
  const at = d.epoch + (d.sub === "validate" ? 1 : Math.min(1, (d.step + 1) / d.steps));
  const pct = Math.min(100, Math.max(0, (at / d.epochs) * 100));
  const stages = (d.stages ?? []).filter((s) => s.until !== null) as { key: string; label: string; until: number }[];
  let from = 0;
  const bands = stages.map((s) => { const b = { ...s, from, to: Math.min(d.epochs, Math.max(from, s.until)) }; from = b.to; return b; }).filter((b) => b.to > b.from);
  const current = bands.find((b) => d.epoch < b.to) ?? bands[bands.length - 1];
  const lossBad = d.loss === "nan" || d.loss === "inf";
  const primary = metrics.find((m) => m.key === r.primaryMetricKey);
  const durable = checkpoints.filter((c) => c.location === "durable").sort((a, b) => secs(b.createdAt) - secs(a.createdAt));
  const resumePoint = durable.reduce((m, c) => Math.max(m, Number(c.step)), 0);
  const lastUpload = durable[0];

  return (
    <div className="rounded-lg border border-primary/40 bg-primary/5 p-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div className="text-sm">
          <span className="mr-2 text-xs font-semibold uppercase tracking-wide text-primary">{t("4 · Training")}</span>
          <b>{t("epoch {e}/{n}", { e: d.epoch + 1, n: d.epochs })}</b>
          <span className="text-fg-muted"> · {d.sub === "validate"
            ? t("validating {i}/{n}", { i: (d.val_step ?? 0) + 1, n: d.val_steps ?? "?" })
            : t("step {i}/{n}", { i: d.step + 1, n: d.steps })}</span>
          <span className={`ml-2 rounded px-1.5 py-0.5 text-xs ${d.sub === "validate" ? "bg-amber-100 text-amber-800" : "bg-sky-100 text-sky-800"}`}>
            {d.sub === "validate" ? t("validating") : t("training")}</span>
        </div>
        <div className="text-sm tabular-nums">
          <b>{pct.toFixed(0)}%</b>
          {progress.etaSeconds > 0 && <span className="text-fg-muted"> · {t("about {d} left", { d: dur(progress.etaSeconds) })}</span>}
        </div>
      </div>

      {/* Stage strip with a cursor: width of each stage = its epochs. */}
      <div className="relative mt-3">
        <div className="flex h-7 overflow-hidden rounded-md border border-line text-[11px]">
          {(bands.length ? bands : [{ key: "all", label: "", from: 0, to: d.epochs }]).map((b) => (
            <div key={b.key} style={{ width: `${((b.to - b.from) / d.epochs) * 100}%` }}
              className={`flex items-center truncate border-r border-line px-2 last:border-0 ${b === current ? "bg-primary/15 font-medium" : "bg-surface-2 text-fg-muted"}`}
              title={b.label ? `${t(b.label)} · ${t("epoch {a}–{b}", { a: b.from + 1, b: b.to })}` : undefined}>
              {b.label ? t(b.label) : ""}</div>))}
        </div>
        <div className="pointer-events-none absolute inset-y-0 w-0.5 bg-primary" style={{ left: `calc(${pct}% - 1px)` }} />
      </div>
      {bands.length > 1 && <div className="mt-1 flex justify-between text-[11px] text-fg-subtle">
        {bands.map((b) => <span key={b.key}>{t("epoch {a}–{b}", { a: b.from + 1, b: b.to })}</span>)}</div>}

      <div className="mt-3 grid gap-2 md:grid-cols-3">
        <Cell icon={<Target className="h-3.5 w-3.5" />} title={t("Objective")} source={t("train · this step")}>
          {d.loss === undefined ? <span className="text-fg-subtle">—</span>
            : <span className={`text-lg font-semibold tabular-nums ${lossBad ? "text-danger" : ""}`}>{lossBad ? String(d.loss).toUpperCase() : metricText(d.loss as number)}</span>}
          {lossBad && <div className="text-xs text-fg-muted">{t("The loss is no longer a number — training stops and is not retried.")}</div>}
        </Cell>
        <Cell icon={<CheckCircle2 className="h-3.5 w-3.5" />} title={t("Validation")} source={t("val · during training")}>
          {primary ? <>
            <div className="text-xs text-fg-muted">{r.primaryMetricKey}</div>
            <span className="text-lg font-semibold tabular-nums">{metricText(primary.last)}</span>
            <span className="ml-1 text-xs text-fg-muted">{t("best {v}", { v: metricText(lower ? primary.min : primary.max) })}</span>
            <div className="mt-1 flex flex-wrap gap-x-3 text-xs text-fg-muted">
              {VAL_KEYS.map((k) => { const m = metrics.find((x) => x.key === k); return m ? <span key={k}>{k.slice(4)} <b className="tabular-nums text-fg">{metricText(m.last)}</b></span> : null; })}
            </div>
          </> : null}
        </Cell>
        <Cell icon={<HardDriveDownload className="h-3.5 w-3.5" />} title={t("Checkpoints")} source={t("stored off the machine")}>
          {lastUpload ? <>
            <div className="text-sm"><span className="mono text-xs">{lastUpload.name}</span> · {t("uploaded {d} ago", { d: dur(Math.max(0, now - secs(lastUpload.createdAt))) })}</div>
            <div className="mt-1 text-xs text-fg-muted">{t("resumes from epoch {n}", { n: resumePoint + 1 })}</div>
          </> : <div className="text-xs text-fg-muted">{t("no checkpoint stored yet")}</div>}
        </Cell>
      </div>

      {(d.completed_epochs >= 1 || primary) && <FirstEpoch d={d} r={r} primary={primary} uploaded={durable.length > 0} eta={progress.etaSeconds} />}
    </div>
  );
}

function Cell({ icon, title, source, children }: { icon: ReactNode; title: string; source: string; children: ReactNode }) {
  return (
    <div className="rounded-md border border-line bg-surface p-2">
      <div className="mb-1 flex items-center justify-between gap-2 text-xs">
        <span className="inline-flex items-center gap-1 font-medium">{icon}{title}</span><span className="text-fg-subtle">{source}</span>
      </div>
      {children}
    </div>
  );
}

/** R27: after the first epoch — is the main metric a number, is a checkpoint stored, how long does an epoch take, when will it finish. */
function FirstEpoch({ d, r, primary, uploaded, eta }: { d: TrainDetail; r: Run; primary?: MetricInfo; uploaded: boolean; eta: number }) {
  const finite = !!primary && Number.isFinite(primary.last);
  const rows: { ok: boolean | null; text: string }[] = [
    { ok: primary ? finite : null, text: primary ? t("{key} is a number ({v})", { key: r.primaryMetricKey, v: metricText(primary.last) }) : t("{key} not reported yet", { key: r.primaryMetricKey || t("main metric") }) },
    { ok: uploaded ? true : null, text: uploaded ? t("a checkpoint is stored off the machine (sha256 recorded)") : t("no checkpoint stored yet") },
    { ok: d.epoch_seconds ? true : null, text: `${t("Epoch time")}: ${d.epoch_seconds ? dur(d.epoch_seconds) : "—"}` },
  ];
  return (
    <div className="mt-3 rounded-md border border-line bg-surface p-2 text-xs">
      <div className="mb-1 font-medium">{t("First epoch check")}</div>
      <ul className="space-y-0.5">
        {rows.map((x) => <li key={x.text} className="flex items-center gap-1.5">
          {x.ok === true ? <CheckCircle2 className="h-3.5 w-3.5 text-emerald-600" /> : x.ok === false ? <TriangleAlert className="h-3.5 w-3.5 text-danger" /> : <CircleDashed className="h-3.5 w-3.5 text-fg-subtle" />}
          <span>{x.text}</span></li>)}
        {eta > 0 && <li className="flex items-center gap-1.5"><RotateCw className="h-3.5 w-3.5 text-fg-subtle" />{t("finishes in about {d} at this pace", { d: dur(eta) })}</li>}
      </ul>
    </div>
  );
}
