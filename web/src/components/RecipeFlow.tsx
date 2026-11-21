import { useState, type ReactNode } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Box, Cpu, Database, FlaskConical, Package, RotateCw } from "lucide-react";
import { api, errorText } from "../lib/api";
import { Dialog, ErrorBox, RelTime } from "./ui";
import { translate as t } from "../i18n";
import type { Recipe } from "../gen/mlplatform/v1/catalog_pb";

type Def = { type?: string | string[]; default?: unknown; pane?: string; label?: string; help?: string; from?: string };
type M = {
  environment?: { gpu?: string; minVramGb?: number }; data?: { input?: string };
  params?: Record<string, Def>; variants?: { name: string; displayName?: string }[];
  canvas?: { phases?: { key: string; label?: string; untilKey?: string }[]; blocks?: { key: string; label?: string }[] };
  entrypoints?: { train?: { fromScratch?: boolean; checkpoints?: string | string[]; progress?: { pattern?: string } } };
  metrics?: { primary?: string }; artifacts?: { best?: { from?: string | string[]; as?: string } };
  failures?: { reason: string }[];
};
const PANES = [["batch", "Batch & augmentation"], ["learn", "What is trained"], ["objective", "Objective & optimizer"], ["schedule", "Training schedule"]] as const;
const DEFAULT_PANE: Record<string, string> = { batch: "batch", epochs: "schedule" };
const list = (x: string | string[] | undefined) => (Array.isArray(x) ? x : x ? [x] : []);
const file = (p: string) => p.replace(/^\{output\}\//, "");
const value = (v: unknown) => (v === null || v === undefined ? t("from the variant") : typeof v === "boolean" ? (v ? t("on") : t("off")) : String(v));

/**
 * S-17-2 (round 1 §8.7): the recipe's flow as the project canvas draws it, read-only — what runs where, which settings each pane offers and
 * their defaults, the stages, what is checkpointed and packaged. Editing happens in a project ("Use for a project…").
 */
export function RecipeFlow({ recipe }: { recipe: Recipe }) {
  let m: M = {};
  try { m = JSON.parse(recipe.manifestJson || "{}") as M; } catch { /* shown empty */ }
  const params = Object.entries(m.params ?? {}).filter(([, d]) => !d.from);
  const paneOf = (k: string, d: Def) => (PANES.some(([p]) => p === d.pane) ? d.pane! : DEFAULT_PANE[k] ?? "objective");
  const train = m.entrypoints?.train;
  const Node = ({ n, icon: Icon, title, children }: { n: number; icon: typeof Box; title: string; children?: ReactNode }) => (
    <div className="rounded-lg border border-line bg-surface p-3">
      <div className="mb-1 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-fg-muted"><Icon className="h-3.5 w-3.5" />{n} · {title}</div>
      {children && <div className="text-sm">{children}</div>}
    </div>);
  return (
    <div className="grid gap-3 lg:grid-cols-[14rem_1fr_13rem]">
      <div className="space-y-3">
        <Node n={1} icon={Cpu} title={t("Environment & machine")}>
          {m.environment?.gpu === "required" ? t("needs a GPU") : t("GPU optional")}{m.environment?.minVramGb ? ` · ≥ ${m.environment.minVramGb} GB VRAM` : ""}
        </Node>
        <Node n={2} icon={Database} title={t("Data")}>
          {m.data?.input ? t("reads {f}", { f: m.data.input.toUpperCase() }) : "—"}
        </Node>
        <Node n={3} icon={Box} title={t("Model & starting point")}>
          {t("{n} variants", { n: m.variants?.length ?? 0 })}
          <div className="text-xs text-fg-muted">{[t("Fine-tune"), t("Train again"), train?.fromScratch ? t("backbone only") : ""].filter(Boolean).join(" · ")}</div>
        </Node>
      </div>

      <div className="rounded-lg border border-primary/40 bg-primary/5 p-3">
        <div className="mb-2 flex flex-wrap items-center gap-2 text-xs font-semibold uppercase tracking-wide text-primary"><RotateCw className="h-3.5 w-3.5" />4 · {t("Training")}</div>
        {(m.canvas?.phases?.length ?? 0) > 0 && (
          <div className="mb-3 flex overflow-hidden rounded-md border border-line text-xs">
            {m.canvas!.phases!.map((p, i) => <div key={p.key} className={`flex-1 truncate px-2 py-1 ${i === 0 ? "bg-primary/15" : "bg-surface-2"}`}>{t(p.label ?? p.key)}</div>)}
          </div>)}
        <div className="grid gap-2 md:grid-cols-2">
          {PANES.map(([key, label]) => {
            const mine = params.filter(([k, d]) => paneOf(k, d) === key);
            return (
              <div key={key} className="rounded-md border border-line bg-surface p-2">
                <div className="mb-1 text-xs font-medium">{t(label)}</div>
                {key === "learn" && (m.canvas?.blocks?.length ?? 0) > 0 && <div className="mb-1 text-xs text-fg-muted">{m.canvas!.blocks!.map((b) => t(b.label ?? b.key)).join(" → ")}</div>}
                {mine.length > 0 && (
                  <table className="w-full text-xs"><tbody>{mine.map(([k, d]) => (
                    <tr key={k}><td className="py-0.5 pr-2">{d.label ? t(d.label) : k}<span className="ml-1 font-mono text-fg-subtle">{k}</span></td>
                      <td className="py-0.5 text-right tabular-nums text-fg-muted">{value(d.default)}</td></tr>))}</tbody></table>)}
              </div>);
          })}
        </div>
        <div className="mt-2 grid gap-2 text-xs text-fg-muted md:grid-cols-2">
          <div>{t("Validation")}: <b className="text-fg">{m.metrics?.primary ?? "—"}</b></div>
          <div>{t("Checkpoints")}: <span className="font-mono">{list(train?.checkpoints).map(file).join(", ") || "—"}</span></div>
          {(m.failures?.length ?? 0) > 0 && <div className="md:col-span-2">{t("Stops for good on: {r}", { r: m.failures!.map((f) => t(f.reason)).join(", ") })}</div>}
        </div>
      </div>

      <div className="space-y-3">
        <Node n={5} icon={Package} title={t("Package")}>
          <span className="font-mono text-xs">{list(m.artifacts?.best?.from).join(" | ") || "—"}</span>
        </Node>
        <Node n={6} icon={FlaskConical} title={t("Evaluate")} />
      </div>
    </div>
  );
}

/** "Used in": projects of the workspace that work with this recipe. */
export function RecipeUsage({ ws, recipe, go }: { ws: string; recipe: string; go: (p: string) => void }) {
  const q = useQuery({ queryKey: ["recipe-usage", ws, recipe], queryFn: () => api.tracking.listRecipeUsage({ workspaceId: ws, recipe }) });
  if (q.error) return <ErrorBox error={errorText(q.error)} />;
  const rows = q.data?.usages ?? [];
  if (!q.data) return <div className="skeleton h-24" />;
  if (rows.length === 0) return <div className="card text-sm text-fg-muted">{t("No project uses this recipe yet.")}</div>;
  return (
    <div className="card overflow-x-auto p-0">
      <table className="w-full text-sm">
        <thead className="border-b border-line text-left text-xs text-fg-muted"><tr>
          <th className="px-4 py-2 font-medium">{t("Project")}</th><th className="px-4 py-2 font-medium">{t("Latest revision")}</th>
          <th className="px-4 py-2 text-right font-medium">{t("Runs")}</th><th className="px-4 py-2 font-medium">{t("Last run")}</th></tr></thead>
        <tbody>{rows.map((u) => (
          <tr key={u.projectRecipeId} className="cursor-pointer border-b border-line last:border-0 hover:bg-surface-2" onClick={() => go(`projects/${u.projectId}?tab=recipe`)}>
            <td className="px-4 py-2 font-medium">{u.projectName}</td>
            <td className="px-4 py-2 font-mono text-xs">{u.latestRevision > 0 ? `r${u.latestRevision}` : "—"}</td>
            <td className="px-4 py-2 text-right tabular-nums">{Number(u.runCount)}</td>
            <td className="px-4 py-2 text-xs text-fg-muted">{u.lastRunAt ? <RelTime at={u.lastRunAt} /> : "—"}</td>
          </tr>))}</tbody>
      </table>
    </div>
  );
}

/** "Use for a project…": pick a project whose task the recipe supports; its canvas opens with the recipe adopted. */
export function UseForProjectSheet({ ws, recipe, onClose, go }: { ws: string; recipe: Recipe; onClose: () => void; go: (p: string) => void }) {
  const projects = useQuery({ queryKey: ["projects", ws], queryFn: () => api.tracking.listProjects({ workspaceId: ws }) });
  const fits = (projects.data?.projects ?? []).filter((p) => recipe.tasks.includes(p.task));
  const [pick, setPick] = useState("");
  const adopt = useMutation({
    mutationFn: (projectId: string) => api.tracking.adoptRecipe({ workspaceId: ws, projectId, recipe: recipe.name }),
    onSuccess: (pr) => go(`projects/${pr.projectId}?tab=recipe`),
  });
  return (
    <Dialog title={t("Use {r} for a project", { r: recipe.displayName })} onClose={onClose}>
      <div className="space-y-3 text-sm">
        {projects.data && fits.length === 0 && <div className="text-fg-muted">{t("No project has task {task}. Create one on the Projects page first.", { task: recipe.tasks.join(", ") })}</div>}
        <div className="space-y-1">{fits.map((p) => (
          <label key={p.id} className={`flex cursor-pointer items-center gap-2 rounded-md border px-3 py-2 ${pick === p.id ? "border-primary bg-primary/10" : "border-line hover:bg-surface-2"}`}>
            <input type="radio" name="project" checked={pick === p.id} onChange={() => setPick(p.id)} />
            <span className="font-medium">{p.name}</span><span className="text-xs text-fg-muted">{p.task}</span>
          </label>))}</div>
        <ErrorBox error={adopt.error ? errorText(adopt.error) : null} />
        <div className="flex justify-end gap-2">
          <button className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-primary" disabled={!pick || adopt.isPending} onClick={() => adopt.mutate(pick)}>{t("Open the recipe in this project")}</button>
        </div>
      </div>
    </Dialog>
  );
}
