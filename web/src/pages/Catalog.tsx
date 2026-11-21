import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { CommandBlock, ErrorBox, PageHeader, StateBadge , Tabs } from "../components/ui";
import { HubModelBrowser } from "../components/HuggingFace";
import { RecipeFlow, RecipeUsage, UseForProjectSheet } from "../components/RecipeFlow";
import { RecipeFilesView } from "../components/RecipeInspect";
import type { Recipe } from "../gen/mlplatform/v1/catalog_pb";
import { translate as t } from "../i18n";

const modality: Record<string, string> = { image: "Image", text: "Text", audio: "Audio", tabular: "Tabular" };

/** S-17-1 Model Catalog — grouped by modality; all model families are equal (D-20, D-25). */
export function CatalogPage({ ws, onOpen, go }: { ws: string; onOpen: (name: string) => void; go: (p: string) => void }) {
  const tab = new URLSearchParams(window.location.search).get("tab") === "hf" ? "hf" : "recipes";
  const recipes = useQuery({ queryKey: ["recipes", ws], queryFn: () => api.catalog.listRecipes({ workspaceId: ws }) });
  const tasks = useQuery({ queryKey: ["tasks"], queryFn: () => api.catalog.listTasks({}) });
  const [filter, setFilter] = useState("");
  const taskModality = new Map((tasks.data?.tasks ?? []).map((t) => [t.key, t.modalityIn]));
  const groups = new Map<string, Recipe[]>();
  for (const r of recipes.data?.recipes ?? []) {
    if (filter && !`${r.name} ${r.displayName} ${r.tasks.join(" ")}`.toLowerCase().includes(filter.toLowerCase())) continue;
    const g = t(modality[taskModality.get(r.tasks[0]) ?? ""] ?? "Other");
    groups.set(g, [...(groups.get(g) ?? []), r]);
  }
  return (
    <>
      <PageHeader title="Model Catalog"
        actions={tab === "recipes" ? <input className="input w-64" placeholder={t("Search recipes, tasks…")} value={filter} onChange={(e) => setFilter(e.target.value)} /> : undefined} />
      <Tabs value={tab} onChange={(t) => go(t === "hf" ? "catalog?tab=hf" : "catalog")} tabs={[{ key: "recipes", label: "Recipe", count: recipes.data?.recipes.length }, { key: "hf", label: "🤗 Hugging Face" }]} />
      {tab === "hf" ? <HubModelBrowser ws={ws} go={go} /> : <>
      <ErrorBox error={recipes.error ? errorText(recipes.error) : null} />
      {[...groups.entries()].map(([g, list]) => (
        <section key={g} className="mb-6">
          <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-500">{g}</h2>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4 min-[1900px]:grid-cols-5 min-[2400px]:grid-cols-6">
            {list.map((r) => (
              <button key={r.id} className="card text-left transition hover:border-brand-500" onClick={() => onOpen(r.name)}>
                <div className="flex items-start justify-between gap-2"><div className="font-medium">{r.displayName}</div><StateBadge state={r.state} /></div>
                <div className="mt-1 text-xs text-slate-500">{r.tasks.join(", ")} · {t("{n} variants", { n: r.variants.length })}</div>
                <div className="mt-2 flex flex-wrap gap-1">
                  <span className="badge bg-slate-100 text-slate-600">{r.licenseCode}</span>
                  {r.requiresAck && <span className="badge bg-amber-100 text-amber-700">⚠ license</span>}
                  {r.maturity === "experimental" && <span className="badge bg-violet-100 text-violet-700">{t("experimental")}</span>}
                  {r.variants.some((v) => v.requiresZoo.length > 0) && <span className="badge bg-slate-100 text-slate-600">🔒 file zoo</span>}
                </div>
              </button>
            ))}
          </div>
        </section>
      ))}
      </>}
    </>
  );
}

/** S-17-2 Recipe — variants, license, command (preview rendered with RenderTemplate). */
type RTab = "flow" | "variants" | "code" | "usage";
const RTABS: RTab[] = ["flow", "variants", "code", "usage"];

/** S-17-2 (round 1 §8.7): Flow (read-only canvas) · Variants · Full text · Used in; the main action takes the recipe into a project. */
export function RecipePage({ ws, name, onBack, go }: { ws: string; name: string; onBack: () => void; go: (p: string) => void }) {
  const r = useQuery({ queryKey: ["recipe", ws, name], queryFn: () => api.catalog.getRecipe({ workspaceId: ws, name }) });
  const [ep, setEp] = useState("train");
  const [scratch, setScratch] = useState(false);
  const [using, setUsing] = useState(false);
  const [tab, setTabState] = useState<RTab>(() => { const q = new URLSearchParams(window.location.search).get("tab") as RTab; return RTABS.includes(q) ? q : "flow"; });
  const setTab = (v: RTab) => {
    setTabState(v);
    const u = new URL(window.location.href);
    if (v === "flow") u.searchParams.delete("tab"); else u.searchParams.set("tab", v);
    window.history.replaceState(window.history.state, "", u);
  };
  const preview = useQuery({
    queryKey: ["render", ws, name, ep, scratch],
    queryFn: () => api.catalog.renderTemplate({ workspaceId: ws, recipe: name, entrypoint: ep, weightsEmpty: scratch }),
    enabled: !!r.data && r.data.entrypoints.includes(ep) && tab === "variants",
  });
  const usage = useQuery({ queryKey: ["recipe-usage", ws, name], queryFn: () => api.tracking.listRecipeUsage({ workspaceId: ws, recipe: name }) });
  if (!r.data) return <ErrorBox error={r.error ? errorText(r.error) : null} />;
  const x = r.data;
  const trains = x.entrypoints.includes("train");
  return (
    <>
      <button className="mb-3 text-sm text-primary" onClick={onBack}>← Catalog</button>
      <PageHeader title={x.displayName} subtitle={<span>{x.source} · {x.tasks.join(", ")} · <StateBadge state={x.state} /></span>}
        actions={trains ? <button className="btn-primary" onClick={() => setUsing(true)}>{t("Use for a project…")}</button> : undefined} />
      <p className="mb-4 max-w-3xl text-sm text-fg-muted">{t(x.description)}</p>
      {(x.licenseNotes || x.requiresAck) && <div className="mb-4 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800"><b>License {x.licenseCode}.</b> {x.licenseNotes}</div>}
      <Tabs<RTab> value={tab} onChange={setTab} tabs={[{ key: "flow", label: t("Flow") }, { key: "variants", label: t("Variants"), count: x.variants.length },
        { key: "code", label: t("Full text") }, { key: "usage", label: t("Used in"), count: usage.data?.usages.length }]} />
      {tab === "flow" && (trains ? <RecipeFlow recipe={x} /> : <div className="card text-sm text-fg-muted">{t("This recipe does not train; it only serves or exports.")}</div>)}
      {tab === "code" && <RecipeFilesView ws={ws} recipe={name} />}
      {tab === "usage" && <RecipeUsage ws={ws} recipe={name} go={go} />}
      {tab === "variants" && <>
      <div className="card mb-4 overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-left text-xs text-fg-muted"><tr><th className="py-1">{t("Variant")}</th><th>Task</th><th>{t("Parameters (M)")}</th><th>{t("Published metrics")}</th><th>License</th><th>File zoo</th></tr></thead>
          <tbody>{x.variants.map((v) => (
            <tr key={v.name} className="border-t border-line"><td className="py-1.5 font-medium">{v.displayName}</td><td>{v.tasks.join(", ")}</td>
              <td>{v.paramsM || "—"}</td><td className="text-xs">{v.reportedJson || "—"}</td><td className="text-xs">{v.license}</td><td className="text-xs">{v.requiresZoo.join(", ") || "—"}</td></tr>))}</tbody>
        </table>
      </div>
      <div className="card space-y-3">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium">{t("Command")}</span>
          {x.entrypoints.filter((e) => !["serve", "assist"].includes(e)).map((e) => (
            <button key={e} className={ep === e ? "btn-primary" : "btn-ghost"} onClick={() => setEp(e)}>{e}</button>))}
          {ep === "train" && <label className="ml-auto flex items-center gap-1 text-xs"><input type="checkbox" checked={scratch} onChange={(e) => setScratch(e.target.checked)} /> {t("train from scratch")}</label>}
        </div>
        {preview.data?.error ? <ErrorBox error={preview.data.error} /> : <CommandBlock text={preview.data?.command ?? "…"} />}
      </div>
      </>}
      {using && <UseForProjectSheet ws={ws} recipe={x} onClose={() => setUsing(false)} go={go} />}
    </>
  );
}
