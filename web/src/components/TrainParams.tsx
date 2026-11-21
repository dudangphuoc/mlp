import { ChevronDown, Maximize2, Minus, Plus, RotateCcw } from "lucide-react";
import { useState } from "react";
import { translate as t } from "../i18n";

/** Manifest parameter definition (same shape RecipeCanvas uses). */
export type PDef = { type?: string | string[]; default?: unknown; enum?: unknown[]; label?: string; pane?: string; min?: number; max?: number };
type Raw = Record<string, string | boolean | undefined>;

const CORE = [/^epochs?$/, /^batch(_size)?$/, /^(imgsz|img_size|image_size|input_size)$/, /^(lr0?|learning_rate|base_lr)$/];
const isCore = (k: string) => CORE.some((r) => r.test(k));
const kindOf = (d: PDef) => (Array.isArray(d.type) ? d.type.find((x) => x !== "null") : d.type) ?? (typeof d.default === "boolean" ? "boolean" : typeof d.default === "number" ? (Number.isInteger(d.default) ? "integer" : "number") : "string");
const GROUP: Record<string, string> = { batch: "Input data", learn: "Trained layers", objective: "Optimizer", schedule: "Schedule", validation: "Validation", checkpoint: "Checkpoint" };

/** Next value for the −/+ buttons: powers of two for batch and image size, ×/÷2 for rates, ±1/±5/±10 for counts. */
function stepValue(k: string, d: PDef, cur: number, dir: 1 | -1): number {
  const clamp = (v: number) => Math.min(d.max ?? Infinity, Math.max(d.min ?? (kindOf(d) === "integer" ? 1 : 0), v));
  if (/batch|imgsz|img_size|image_size|input_size/.test(k)) {
    const step = /batch/.test(k) ? 0 : 32;
    if (step) return clamp(Math.round((cur + dir * step) / step) * step);
    return clamp(dir > 0 ? Math.max(1, cur * 2) : Math.max(1, Math.floor(cur / 2)));
  }
  if (kindOf(d) === "number") return clamp(Number((dir > 0 ? cur * 2 : cur / 2).toPrecision(3)));
  const s = cur >= 100 ? 10 : cur >= 20 ? 5 : 1;
  return clamp(cur + dir * s);
}

function Stepper({ k, d, value, error, changed, disabled, placeholder, onChange }: { k: string; d: PDef; value: string | boolean | undefined; error?: string; changed: boolean; disabled: boolean; placeholder?: string; onChange: (v: string | undefined) => void }) {
  const text = value === undefined || value === "" ? (d.default === null || d.default === undefined ? "" : String(d.default)) : String(value);
  const n = Number(text);
  const base = Number.isFinite(n) && text !== "" ? n : Number(placeholder ?? d.default ?? 0);
  const step = (dir: 1 | -1) => onChange(String(stepValue(k, d, Number.isFinite(base) ? base : 0, dir)));
  return (
    <div className={`rounded-xl border p-3 ${error ? "border-danger" : changed ? "border-primary/60 bg-primary/5" : "border-line bg-surface-2"}`}>
      <div className="mb-2 flex items-center justify-between gap-2 text-xs text-fg-muted">
        <span className="truncate">{d.label ? t(d.label) : k}</span>
        {changed && <button type="button" className="inline-flex h-7 w-7 items-center justify-center rounded-md text-primary hover:bg-primary/10" aria-label={t("Reset")} onClick={() => onChange(undefined)}><RotateCcw className="h-3.5 w-3.5" /></button>}
      </div>
      <div className="flex items-center gap-2">
        <button type="button" disabled={disabled} aria-label={`${k} −`} onClick={() => step(-1)} className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg border border-line bg-surface text-fg active:scale-95 disabled:opacity-40"><Minus className="h-5 w-5" /></button>
        <input className="h-11 min-w-0 flex-1 rounded-lg border border-line bg-surface px-2 text-center text-lg font-semibold tabular-nums focus:border-primary focus:outline-none" inputMode="decimal"
          disabled={disabled} aria-label={k} aria-invalid={!!error} value={text} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
        <button type="button" disabled={disabled} aria-label={`${k} +`} onClick={() => step(1)} className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg border border-line bg-surface text-fg active:scale-95 disabled:opacity-40"><Plus className="h-5 w-5" /></button>
      </div>
      {error && <div className="mt-1.5 text-xs text-danger">{error}</div>}
    </div>
  );
}

function Toggle({ k, d, value, changed, disabled, onChange }: { k: string; d: PDef; value: string | boolean | undefined; changed: boolean; disabled: boolean; onChange: (v: boolean) => void }) {
  const on = value === undefined || value === "" ? Boolean(d.default) : value === true || value === "true";
  return (
    <button type="button" aria-pressed={on} disabled={disabled} onClick={() => onChange(!on)}
      className={`flex h-11 items-center gap-2 rounded-full border px-4 text-sm active:scale-95 disabled:opacity-40 ${on ? "border-primary bg-primary/15 text-primary" : "border-line bg-surface-2 text-fg-muted"} ${changed ? "ring-1 ring-primary/50" : ""}`}>
      <span className={`h-2.5 w-2.5 rounded-full ${on ? "bg-primary" : "bg-fg-subtle/40"}`} />{d.label ? t(d.label) : k}
    </button>
  );
}

function Choice({ k, d, value, changed, disabled, onChange }: { k: string; d: PDef; value: string | boolean | undefined; changed: boolean; disabled: boolean; onChange: (v: string) => void }) {
  const cur = String(value ?? d.default ?? "");
  const opts = (d.enum ?? []).map(String);
  return (
    <div className={`rounded-xl border p-3 ${changed ? "border-primary/60 bg-primary/5" : "border-line bg-surface-2"}`}>
      <div className="mb-2 truncate text-xs text-fg-muted">{d.label ? t(d.label) : k}</div>
      {opts.length <= 4 ? (
        <div className="flex flex-wrap gap-2">{opts.map((o) => (
          <button key={o} type="button" disabled={disabled} aria-pressed={o === cur} onClick={() => onChange(o)}
            className={`h-11 min-w-[3rem] rounded-lg border px-3 text-sm active:scale-95 ${o === cur ? "border-primary bg-primary/15 font-medium text-primary" : "border-line bg-surface text-fg-muted"}`}>{o}</button>))}</div>
      ) : (
        <select className="input h-11 text-base" aria-label={k} disabled={disabled} value={cur} onChange={(e) => onChange(e.target.value)}>
          {opts.map((o) => <option key={o}>{o}</option>)}</select>)}
    </div>
  );
}

function Field(p: { k: string; d: PDef; value: string | boolean | undefined; error?: string; changed: boolean; disabled: boolean; placeholder?: string; onChange: (v: string | boolean | undefined) => void }) {
  const kd = kindOf(p.d);
  if (p.d.enum) return <Choice {...p} />;
  if (kd === "boolean") return <Toggle {...p} />;
  if (kd === "integer" || kd === "number") return <Stepper {...p} />;
  return (
    <div className={`rounded-xl border p-3 ${p.changed ? "border-primary/60 bg-primary/5" : "border-line bg-surface-2"}`}>
      <div className="mb-2 truncate text-xs text-fg-muted">{p.d.label ? t(p.d.label) : p.k}</div>
      <input className="input h-11 text-base" aria-label={p.k} disabled={p.disabled} value={p.value === undefined ? String(p.d.default ?? "") : String(p.value)} onChange={(e) => p.onChange(e.target.value)} />
      {p.error && <div className="mt-1.5 text-xs text-danger">{p.error}</div>}
    </div>
  );
}

/**
 * The training parameters as touch-sized controls: the everyday ones first (epochs, batch, image size, learning rate), switches for
 * augmentation, everything else folded under Advanced (grouped as the recipe declares them; the details icon opens the drawings/config/code sheet).
 */
export function ParamBoard({ defs, raw, errors, changed, paneOf, disabled, placeholders, onChange, onReset, onDetails }: {
  defs: [string, PDef][]; raw: Raw; placeholders?: Record<string, string | undefined>; errors: Record<string, string>; changed: (k: string) => boolean; paneOf: (k: string, d: PDef) => string;
  disabled: boolean; onChange: (k: string, v: string | boolean | undefined) => void; onReset: (k: string) => void; onDetails: (pane: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const core = defs.filter(([k]) => isCore(k));
  const aug = defs.filter(([k, d]) => !isCore(k) && kindOf(d) === "boolean" && paneOf(k, d) === "batch");
  const rest = defs.filter(([k, d]) => !isCore(k) && !(kindOf(d) === "boolean" && paneOf(k, d) === "batch"));
  const groups = Object.keys(GROUP).map((g) => [g, rest.filter(([k, d]) => paneOf(k, d) === g)] as const).filter(([, l]) => l.length > 0);
  const f = (k: string, d: PDef) => <Field key={k} k={k} d={d} value={raw[k]} error={errors[k]} changed={changed(k)} disabled={disabled} placeholder={placeholders?.[k]} onChange={(v) => (v === undefined ? onReset(k) : onChange(k, v))} />;
  return (
    <div className="space-y-4">
      {core.length > 0 && <div className="grid gap-3 @lg:grid-cols-2 @3xl:grid-cols-4">{core.map(([k, d]) => f(k, d))}</div>}
      {aug.length > 0 && (
        <div>
          <div className="mb-2 text-xs font-medium text-fg-muted">{t("Augmentation")}</div>
          <div className="flex flex-wrap gap-2">{aug.map(([k, d]) => f(k, d))}</div>
        </div>)}
      {groups.length > 0 && (
        <div className="rounded-xl border border-line">
          <button type="button" onClick={() => setOpen(!open)} className="flex h-12 w-full items-center gap-2 px-4 text-sm font-medium">
            <ChevronDown className={`h-4 w-4 transition ${open ? "" : "-rotate-90"}`} />{t("Advanced")}
            <span className="ml-auto text-xs font-normal text-fg-muted">{rest.length}</span></button>
          {open && (
            <div className="space-y-4 border-t border-line p-4">
              {groups.map(([g, list]) => (
                <div key={g}>
                  <div className="mb-2 flex items-center gap-2 text-xs font-medium text-fg-muted">{t(GROUP[g])}
                    <button type="button" className="ml-auto inline-flex h-9 w-9 items-center justify-center rounded-lg hover:bg-surface-2" aria-label={`${t(GROUP[g])} — ${t("Details")}`} onClick={() => onDetails(g)}><Maximize2 className="h-4 w-4" /></button></div>
                  <div className="grid gap-3 @lg:grid-cols-2 @3xl:grid-cols-3">{list.map(([k, d]) => f(k, d))}</div>
                </div>))}
            </div>)}
        </div>)}
    </div>
  );
}
