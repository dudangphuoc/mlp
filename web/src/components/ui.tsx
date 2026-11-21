import { createContext, useContext, useState, type ReactNode } from "react";
import * as RDialog from "@radix-ui/react-dialog";
import * as Tooltip from "@radix-ui/react-tooltip";
import type { Timestamp } from "@bufbuild/protobuf/wkt";
import { ago, stateColor, ts } from "../lib/format";
import { Check, Copy, Inbox, X, type LucideIcon } from "lucide-react";
import { translate as t } from "../i18n";

/** Icon of the active sidebar entry — PageHeader/EmptyState use it by default so every page shares one icon set. */
export const SectionIcon = createContext<LucideIcon | null>(null);

/* Shared component set — 30/01 §4. Every color goes through tokens (dark/light theme switches automatically). */

const stateLabel: Record<string, string> = {
  online: "online", offline: "offline", busy: "busy", degraded: "degraded", enrolling: "enrolling",
  queued: "queued", scheduling: "scheduling", starting: "starting", running: "running", succeeded: "done", failed: "failed",
  cancelled: "cancelled", stopping: "stopping", stopped: "stopped", skipped: "skipped", active: "active", paused: "paused",
  completed: "completed", draft: "draft", ready: "ready", building: "building",
  unlabeled: "unlabeled", in_progress: "in progress", in_review: "in review", rejected: "returned", accepted: "accepted",
};

/** Status badge — token colors, reason tooltip (StatusBadge 30/01 §4). */
export function StateBadge({ state, reason }: { state: string; reason?: string }) {
  const badge = <span className={`badge ${stateColor[state] ?? "bg-slate-100 text-slate-700"}`}>
    <span className="h-1.5 w-1.5 rounded-full bg-current opacity-80" />{t(stateLabel[state] ?? state)}</span>;
  return reason ? <Tip text={t(reason)}>{badge}</Tip> : badge;
}
export const StatusBadge = StateBadge;

export function Tip({ text, children }: { text: ReactNode; children: ReactNode }) {
  return (
    <Tooltip.Provider delayDuration={250}>
      <Tooltip.Root>
        <Tooltip.Trigger asChild><span className="inline-flex">{children}</span></Tooltip.Trigger>
        <Tooltip.Portal>
          <Tooltip.Content sideOffset={6} className="z-[60] max-w-xs rounded-md border border-line bg-overlay px-2 py-1 text-xs text-fg shadow-lg">
            {text}
          </Tooltip.Content>
        </Tooltip.Portal>
      </Tooltip.Root>
    </Tooltip.Provider>
  );
}

/** Relative time + tooltip with the full timestamp in the viewer's time zone (30/01 §6). */
export function RelTime({ at }: { at?: Timestamp }) {
  const d = ts(at);
  if (!d) return <span className="text-fg-subtle">—</span>;
  return <Tip text={d.toLocaleString("vi-VN")}><span className="whitespace-nowrap">{ago(at)}</span></Tip>;
}

/** Long ID: first 8 characters in mono + copy (30/01 §6). */
export function ShortId({ id }: { id: string }) {
  const [ok, setOk] = useState(false);
  return (
    <button className="mono inline-flex items-center gap-1 rounded px-1 text-xs text-fg-muted hover:bg-surface-2" title={id}
      onClick={() => { void navigator.clipboard.writeText(id); setOk(true); setTimeout(() => setOk(false), 1200); }}>
      {id.slice(0, 8)}{ok ? <Check className="h-3 w-3 text-success" /> : <Copy className="h-3 w-3 text-fg-subtle" />}
    </button>
  );
}

export function PageHeader({ title, subtitle, actions, icon, badges }:
  { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; icon?: ReactNode; badges?: ReactNode }) {
  const Sec = useContext(SectionIcon);
  return (
    <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
      <div className="flex min-w-0 items-start gap-3">
        {(icon ?? Sec) && <div className="mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-surface-2 text-fg-muted">
          {typeof icon === "object" && icon ? icon : Sec ? <Sec className="h-5 w-5" strokeWidth={1.9} /> : null}</div>}
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2"><h1 className="truncate text-xl font-semibold tracking-tight">{title}</h1>{badges}</div>
          {subtitle && <div className="mt-1 text-sm text-fg-muted">{subtitle}</div>}
        </div>
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  );
}

export function EmptyState({ title, children, icon, action, bare }: { title: string; children?: ReactNode; icon?: ReactNode; action?: ReactNode; bare?: boolean }) {
  const Sec = useContext(SectionIcon) ?? Inbox;
  return (
    <div className={`${bare ? "" : "card "}flex flex-col items-center gap-3 py-14 text-center text-fg-muted`}>
      <div className="flex h-12 w-12 items-center justify-center rounded-full bg-surface-2 text-fg-subtle">
        {typeof icon === "object" && icon ? icon : <Sec className="h-6 w-6" strokeWidth={1.7} />}</div>
      <div className="font-medium text-fg">{title}</div>
      {children && <div className="max-w-md text-sm">{children}</div>}
      {action}
    </div>
  );
}

/** Error inside the content area: plain-language message + retry button (30/01 §5). */
export function ErrorBox({ error, onRetry }: { error?: string | null; onRetry?: () => void }) {
  if (!error) return null;
  return (
    <div className="mb-4 flex items-start justify-between gap-3 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">
      <span>{error}</span>
      {onRetry && <button className="shrink-0 underline" onClick={onRetry}>{t("Retry")}</button>}
    </div>
  );
}

/** Skeleton matching the content shape (30/01 §5 — no full-page spinner). */
export function Skeleton({ rows = 3, className = "" }: { rows?: number; className?: string }) {
  return <div className={`space-y-2 ${className}`}>{Array.from({ length: rows }, (_, i) => <div key={i} className="skeleton h-10" />)}</div>;
}

/**
 * Sheet sliding in from the right edge (UI rule: NO centered popups). Every create/edit/confirm/detail form uses this component.
 * Still named Dialog so every page shares it; `wide` = a wider sheet.
 */
export function Dialog({ title, onClose, children, wide, xwide, description }:
  { title: string; onClose: () => void; children: ReactNode; wide?: boolean; xwide?: boolean; description?: ReactNode }) {
  return (
    <RDialog.Root open onOpenChange={(o) => { if (!o) onClose(); }}>
      <RDialog.Portal>
        <RDialog.Overlay className="sheet-overlay fixed inset-0 z-50 bg-black/40" />
        <RDialog.Content className={`sheet-panel fixed inset-y-0 right-0 z-50 flex w-full flex-col border-l border-line bg-overlay shadow-2xl ${xwide ? "sm:max-w-5xl" : wide ? "sm:max-w-2xl" : "sm:max-w-md"}`}>
          <div className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
            <div className="min-w-0">
              <RDialog.Title className="text-base font-semibold">{title}</RDialog.Title>
              {description && <RDialog.Description className="mt-1 text-sm text-fg-muted">{description}</RDialog.Description>}
            </div>
            <RDialog.Close className="-mr-1 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-md text-fg-subtle hover:bg-surface-2 hover:text-fg" aria-label={t("Close")}>
              <X className="h-4 w-4" />
            </RDialog.Close>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5">{children}</div>
        </RDialog.Content>
      </RDialog.Portal>
    </RDialog.Root>
  );
}
export const Sheet = Dialog;

/** Dangerous action: state the consequence, type the resource name to confirm (30/01 §5). */
export function ConfirmDialog({ title, consequence, confirmText, label = "Confirm", onConfirm, onClose, busy }:
  { title: string; consequence: ReactNode; confirmText?: string; label?: string; onConfirm: () => void; onClose: () => void; busy?: boolean }) {
  const [typed, setTyped] = useState("");
  const ok = !confirmText || typed === confirmText;
  return (
    <Dialog title={title} onClose={onClose}>
      <div className="space-y-4 text-sm">
        <div className="text-fg-muted">{consequence}</div>
        {confirmText && <Field label={t("Type \"{x}\" to confirm", { x: confirmText })}><input className="input" value={typed} onChange={(e) => setTyped(e.target.value)} autoFocus /></Field>}
        <div className="flex justify-end gap-2">
          <button className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-danger" disabled={!ok || busy} onClick={onConfirm}>{t(label)}</button>
        </div>
      </div>
    </Dialog>
  );
}

export function CommandBlock({ text, secret }: { text: string; secret?: string }) {
  const [shown, setShown] = useState(false);
  const [copied, setCopied] = useState(false);
  const display = secret && !shown ? text.replaceAll(secret, "•".repeat(12)) : text;
  return (
    <div className="relative rounded-lg border border-line bg-[#0b0e13]">
      <pre className="mono overflow-x-auto whitespace-pre-wrap break-all p-3 pr-28 text-xs" style={{ color: "#e6e9ef" }}>{display}</pre>
      <div className="absolute right-2 top-2 flex gap-1">
        {secret && <button className="btn-ghost px-2 py-0.5 text-xs" onClick={() => setShown(!shown)}>{shown ? t("Hide") : t("Show")}</button>}
        <button className="btn-ghost px-2 py-0.5 text-xs" onClick={() => { void navigator.clipboard.writeText(text); setCopied(true); setTimeout(() => setCopied(false), 1200); }}>
          {copied ? t("Copied") : t("Copy")}
        </button>
      </div>
    </div>
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="label">{label}</span>
      {children}
    </label>
  );
}

/** Linked stat tile (StatCard / KpiStrip). */
export function StatCard({ label, value, sub, onClick, tone }:
  { label: string; value: ReactNode; sub?: ReactNode; onClick?: () => void; tone?: "success" | "warning" | "danger" | "info" }) {
  const toneCls = tone === "danger" ? "text-danger" : tone === "warning" ? "text-warning" : tone === "success" ? "text-success" : "text-fg";
  return (
    <button className="card flex flex-col items-start gap-1 text-left transition hover:border-primary/40 disabled:cursor-default" onClick={onClick} disabled={!onClick}>
      <span className="text-xs font-medium text-fg-muted">{label}</span>
      <span className={`text-2xl font-semibold tabular-nums ${toneCls}`}>{value}</span>
      {sub && <span className="text-xs text-fg-subtle">{sub}</span>}
    </button>
  );
}

/** Tabs with counts; state kept in the URL (?tab=) by the calling page. */
export function Tabs<T extends string>({ tabs, value, onChange }: { tabs: { key: T; label: string; count?: number }[]; value: T; onChange: (t: T) => void }) {
  return (
    <div className="mb-5 flex gap-1 overflow-x-auto border-b border-line [scrollbar-width:none]" role="tablist">
      {tabs.map((t) => (
        <button key={t.key} role="tab" aria-selected={value === t.key} onClick={() => onChange(t.key)}
          ref={value === t.key ? (el) => el?.scrollIntoView({ block: "nearest", inline: "nearest" }) : undefined}
          className={`-mb-px shrink-0 whitespace-nowrap border-b-2 px-3 py-2.5 text-sm ${value === t.key ? "border-primary font-medium text-fg" : "border-transparent text-fg-muted hover:text-fg"}`}>
          {t.label}{t.count !== undefined && <span className="ml-1.5 rounded-full bg-surface-2 px-1.5 text-xs text-fg-muted">{t.count}</span>}
        </button>))}
    </div>
  );
}

/** Hardware chips: GPU + VRAM, CPU, RAM, OS (30/01 §4). */
export function HardwareChip({ children }: { children: ReactNode }) {
  return <span className="inline-flex items-center gap-1 rounded-md border border-line bg-surface-2 px-2 py-0.5 text-xs text-fg-muted">{children}</span>;
}

/** Steps with status (ProgressSteps). */
export function ProgressSteps({ steps }: { steps: { label: string; state: "done" | "active" | "pending" | "failed"; detail?: string }[] }) {
  return (
    <ol className="space-y-2">
      {steps.map((s) => (
        <li key={s.label} className="flex items-start gap-2 text-sm">
          <span className={`mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[11px] ${
            s.state === "done" ? "bg-success/20 text-success" : s.state === "active" ? "bg-info/20 text-info animate-pulse"
              : s.state === "failed" ? "bg-danger/20 text-danger" : "bg-surface-2 text-fg-subtle"}`}>
            {s.state === "done" ? "✓" : s.state === "failed" ? "!" : "•"}
          </span>
          <span className={s.state === "pending" ? "text-fg-subtle" : ""}>{s.label}{s.detail && <span className="ml-2 text-xs text-fg-muted">{s.detail}</span>}</span>
        </li>))}
    </ol>
  );
}
