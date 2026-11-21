import type { Timestamp } from "@bufbuild/protobuf/wkt";
import { getLang, translate } from "../i18n";

export function ts(t?: Timestamp): Date | undefined {
  return t ? new Date(Number(t.seconds) * 1000 + Math.floor(t.nanos / 1e6)) : undefined;
}

export function ago(t?: Timestamp): string {
  const d = ts(t);
  if (!d) return "—";
  const s = Math.round((Date.now() - d.getTime()) / 1000);
  if (s < 60) return translate("{n}s ago", { n: s });
  if (s < 3600) return translate("{n} min ago", { n: Math.round(s / 60) });
  if (s < 86400) return translate("{n} h ago", { n: Math.round(s / 3600) });
  return d.toLocaleDateString(getLang() === "en" ? "en-US" : "vi-VN");
}

const locale = () => (getLang() === "en" ? "en-US" : "vi-VN");

/** Full date and time in the UI language (24-hour clock in Vietnamese). */
export function dateTime(t?: Timestamp): string {
  const d = ts(t);
  return d ? d.toLocaleString(locale(), { hour12: false }) : "—";
}

/** Clock time in the UI language. */
export function clock(ms: number): string {
  return new Date(ms).toLocaleTimeString(locale(), { hour12: false });
}

/** Calendar date in the UI language. */
export function day(ms: number): string {
  return new Date(ms).toLocaleDateString(locale());
}

export function bytes(n: bigint | number): string {
  let v = Number(n);
  const u = ["B", "KB", "MB", "GB", "TB"];
  let i = 0;
  while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
  return `${v.toFixed(v < 10 && i > 0 ? 1 : 0)} ${u[i]}`;
}

export const stateColor: Record<string, string> = {
  online: "bg-emerald-100 text-emerald-700", busy: "bg-sky-100 text-sky-700", offline: "bg-slate-200 text-slate-600",
  degraded: "bg-amber-100 text-amber-700", enrolling: "bg-violet-100 text-violet-700",
  queued: "bg-slate-200 text-slate-700", scheduling: "bg-violet-100 text-violet-700", starting: "bg-violet-100 text-violet-700",
  running: "bg-sky-100 text-sky-700", succeeded: "bg-emerald-100 text-emerald-700", failed: "bg-rose-100 text-rose-700",
  cancelled: "bg-slate-200 text-slate-600", unbuilt: "bg-amber-100 text-amber-700", verified: "bg-emerald-100 text-emerald-700",
  invalid: "bg-rose-100 text-rose-700",
  // M05: labeling item/project states
  unlabeled: "bg-slate-200 text-slate-700", in_progress: "bg-sky-100 text-sky-700", in_review: "bg-violet-100 text-violet-700",
  submitted: "bg-violet-100 text-violet-700", accepted: "bg-emerald-100 text-emerald-700", rejected: "bg-rose-100 text-rose-700",
  skipped: "bg-amber-100 text-amber-700", draft: "bg-slate-200 text-slate-700", active: "bg-emerald-100 text-emerald-700",
  paused: "bg-amber-100 text-amber-700", completed: "bg-slate-200 text-slate-600",
  stopping: "bg-amber-100 text-amber-700", stopped: "bg-slate-200 text-slate-600",
};
