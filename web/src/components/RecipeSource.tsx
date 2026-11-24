import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { GitBranch, GitFork, RefreshCw } from "lucide-react";
import { api, errorText } from "../lib/api";
import { ago } from "../lib/format";
import { ErrorBox } from "./ui";
import type { Recipe } from "../gen/mlplatform/v1/catalog_pb";
import { translate as t } from "../i18n";

type Repo = { git: string; commit: string; path?: string; ref?: string };
type Fetched = { url: string; path: string; sha256?: string };

function repoUrl(git: string, commit: string) {
  return git.includes("github.com") ? `${git.replace(/\.git$/, "")}/tree/${commit}` : git;
}

function shortRepo(git: string) {
  return git.replace(/^https?:\/\/(www\.)?/, "").replace(/\.git$/, "");
}

/** Catalog header: the repository and branch the builtin recipes come from, and a resync. */
export function CatalogSourceBar() {
  const qc = useQueryClient();
  const me = useQuery({ queryKey: ["me"], queryFn: () => api.iam.getMe({}) });
  const isAdmin = !!me.data?.isInstanceAdmin;
  const src = useQuery({ queryKey: ["catalog-source"], queryFn: () => api.catalog.getCatalogSource({}) });
  const sync = useMutation({
    mutationFn: () => api.catalog.syncCatalog({}),
    onSuccess: (d) => { qc.setQueryData(["catalog-source"], d); qc.invalidateQueries({ queryKey: ["recipes"] }); },
  });
  const s = src.data;
  if (!s) return null;
  return (
    <div className="mb-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
      <GitBranch className="h-4 w-4 text-fg-muted" />
      {s.local ? <span className="font-mono text-xs">{s.origin}</span> : <>
        <a className="font-mono text-xs text-primary" href={repoUrl(s.origin, s.commit || s.ref)} target="_blank" rel="noreferrer">{shortRepo(s.origin)}</a>
        <span className="badge bg-slate-100 text-slate-600">{s.ref}</span>
        {s.commit && <span className="font-mono text-xs text-fg-muted">{s.commit.slice(0, 7)}</span>}
      </>}
      <span className="text-xs text-fg-muted">{t("{n} recipes", { n: s.recipes })} · {ago(s.syncedAt)}</span>
      {isAdmin && <button className="btn-ghost inline-flex h-7 items-center gap-1 whitespace-nowrap text-xs" disabled={sync.isPending} onClick={() => sync.mutate()}>
        <RefreshCw className={`h-3.5 w-3.5 ${sync.isPending ? "animate-spin" : ""}`} />{t("Sync")}</button>}
      {(s.error || sync.error) && <div className="w-full"><ErrorBox error={sync.error ? errorText(sync.error) : s.error} /></div>}
    </div>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return <tr className="border-t border-line align-top"><td className="w-44 py-1.5 pr-3 text-fg-muted">{label}</td><td className="py-1.5">{children}</td></tr>;
}

function RepoCell({ r }: { r: Repo }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <a className="font-mono text-xs text-primary" href={repoUrl(r.git, r.commit)} target="_blank" rel="noreferrer">{shortRepo(r.git)}</a>
      {r.ref && <span className="badge bg-slate-100 text-slate-600">{r.ref}</span>}
      <span className="font-mono text-xs text-fg-muted">{r.commit.slice(0, 12)}</span>
      {r.path && <span className="font-mono text-xs text-fg-subtle">→ {r.path}</span>}
      {r.git.includes("github.com") && <a className="btn-ghost inline-flex h-7 items-center gap-1 whitespace-nowrap text-xs" href={`${r.git.replace(/\.git$/, "")}/fork`} target="_blank" rel="noreferrer">
        <GitFork className="h-3.5 w-3.5" />Fork</a>}
    </div>
  );
}

function FileList({ items }: { items: Fetched[] }) {
  return (
    <table className="w-full font-mono text-xs"><tbody>{items.map((f) => (
      <tr key={f.path}>
        <td className="whitespace-nowrap py-0.5 pr-3"><a className="text-primary" href={f.url} target="_blank" rel="noreferrer">{f.url.split("/").pop()}</a></td>
        <td className="py-0.5 pr-3 text-fg-subtle">{f.path}</td>
        <td className="whitespace-nowrap py-0.5 text-fg-subtle">{f.sha256 ? f.sha256.slice(0, 12) : ""}</td>
      </tr>))}
    </tbody></table>
  );
}

/** Recipe page "Source" tab: upstream repository at its commit, files, runtime downloads and the environment. */
export function RecipeSourceView({ recipe }: { recipe: Recipe }) {
  const m = JSON.parse(recipe.manifestJson || "{}");
  const src = m.source ?? {};
  const env = m.environment ?? {};
  const repos: Repo[] = [...(src.git ? [{ git: src.git, commit: src.commit ?? "", ref: src.ref, path: src.path ?? "/opt/recipe/src" }] : []), ...(src.extraRepos ?? [])];
  const pip: string[] = env.pip ?? src.pip ?? [];
  return (
    <div className="card overflow-x-auto">
      <table className="w-full text-sm"><tbody>
        {repos.map((r, i) => <Row key={i} label={i === 0 ? t("Code") : t("Extra repository")}><RepoCell r={r} /></Row>)}
        {(src.files ?? []).length > 0 && <Row label={t("Upstream files")}><FileList items={src.files} /></Row>}
        {(src.downloads ?? []).length > 0 && <Row label={t("Downloaded when the job starts")}><FileList items={src.downloads} /></Row>}
        <Row label={t("Base image")}><span className="font-mono text-xs">{env.base ?? "—"}</span></Row>
        {(env.apt ?? []).length > 0 && <Row label="apt"><span className="font-mono text-xs">{env.apt.join(" ")}</span></Row>}
        {pip.length > 0 && <Row label="pip"><span className="font-mono text-xs">{pip.join(" ")}</span></Row>}
      </tbody></table>
    </div>
  );
}
