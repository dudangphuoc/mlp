import { useQuery } from "@tanstack/react-query";
import { CheckCircle2, TriangleAlert } from "lucide-react";
import { api, errorText } from "../lib/api";
import { ErrorBox } from "./ui";
import { translate as t } from "../i18n";
import type { Run } from "../gen/mlplatform/v1/tracking_pb";

const PANE_ORDER = ["model", "data", "machine", "batch", "learn", "objective", "schedule"];
const PANE_LABEL: Record<string, string> = {
  model: "Model & starting point", data: "Data", machine: "Environment & machine", batch: "Batch & augmentation", learn: "What is trained",
  objective: "Objective & optimizer", schedule: "Training schedule",
};
const FIELD_LABEL: Record<string, string> = {
  recipe: "Recipe", variant: "Variant", revision: "Revision", data: "Dataset version", start: "Starting point", train_kind: "Kind", gpus: "GPU",
};

function shown(field: string, v: string): string {
  if (v === "") return "—";
  if (field === "start") return v === "backbone" ? t("backbone only") : t("pretrained weights");
  if (field === "train_kind") return v.startsWith("retrain") ? `${t("Train again")} ${v.slice(8)}` : t("Fine-tune");
  return v;
}

/**
 * Round 1 §8.6: before the chart is read — are the numbers comparable (same validation set, same axis) — then only the lines that differ,
 * grouped by the canvas pane they belong to.
 */
export function RunComparison({ ws, runs, colorOf }: { ws: string; runs: Run[]; colorOf: (r: Run) => string | undefined }) {
  const ids = runs.map((r) => r.id);
  const q = useQuery({
    queryKey: ["compare-runs", ws, ids.join()], enabled: ids.length >= 2,
    queryFn: () => api.tracking.compareRuns({ workspaceId: ws, runIds: ids }),
  });
  if (ids.length < 2) return null;
  if (q.error) return <ErrorBox error={errorText(q.error)} />;
  const c = q.data;
  if (!c) return <div className="skeleton h-24" />;
  const panes = PANE_ORDER.filter((p) => c.rows.some((r) => r.pane === p)).concat([...new Set(c.rows.map((r) => r.pane))].filter((p) => !PANE_ORDER.includes(p)));
  const Check = ({ ok, yes, no }: { ok: boolean; yes: string; no: string }) => (
    <span className={`inline-flex items-center gap-1 ${ok ? "text-emerald-700" : "text-warning"}`}>
      {ok ? <CheckCircle2 className="h-4 w-4" /> : <TriangleAlert className="h-4 w-4" />}{ok ? yes : no}</span>);
  return (
    <div className="card mt-3 p-0">
      <div className="flex flex-wrap gap-x-5 gap-y-1 border-b border-line px-4 py-2 text-sm">
        <Check ok={c.sameVal} yes={t("same validation set")} no={t("different validation sets — the numbers are not comparable")} />
        <Check ok={c.sameAxis} yes={t("same x axis")} no={t("different x axes (some runs log validation per epoch) — do not read the curves against each other")} />
      </div>
      {c.rows.length === 0 ? <div className="px-4 py-3 text-sm text-fg-muted">{t("These runs used the same recipe, data and settings.")}</div> : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="border-b border-line text-left text-xs text-fg-muted"><tr>
              <th className="px-4 py-1.5 font-medium">{t("Differs")}</th>
              {runs.map((r) => <th key={r.id} className="px-3 py-1.5 font-medium"><span className="mr-1 inline-block h-2 w-2 rounded-full" style={{ background: colorOf(r) }} />#{r.number}</th>)}
            </tr></thead>
            <tbody>{panes.map((p) => [
              <tr key={p} className="bg-surface-2 text-xs text-fg-muted"><td className="px-4 py-1" colSpan={runs.length + 1}>{t(PANE_LABEL[p] ?? p)}</td></tr>,
              ...c.rows.filter((r) => r.pane === p).map((row) => (
                <tr key={row.field} className="border-b border-line last:border-0">
                  <td className="px-4 py-1.5 font-mono text-xs">{row.field.startsWith("params.") ? row.field.slice(7) : t(FIELD_LABEL[row.field] ?? row.field)}</td>
                  {row.values.map((v, i) => <td key={i} className="px-3 py-1.5 tabular-nums">{shown(row.field, v)}</td>)}
                </tr>))])}</tbody>
          </table>
        </div>)}
    </div>
  );
}
