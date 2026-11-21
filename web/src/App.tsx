import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useCrumbNames } from "./lib/crumbs";
import { registerWebMcp } from "./lib/webmcp";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createRootRoute, createRoute, createRouter, Navigate, Outlet, RouterProvider, useNavigate, useParams, useRouterState,
} from "@tanstack/react-router";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { Command } from "cmdk";
import {
  Activity, Boxes, BrainCircuit, Menu as Menu2, Gauge, ChevronRight, ChevronsUpDown, Database, FlaskConical, Languages, LayoutDashboard, LibraryBig, ListChecks, Moon,
  NotebookPen, PanelLeftClose, PanelLeftOpen, Rocket, Search, Server, Settings, SlidersHorizontal, Sun, Table2, Tags, Workflow,
  type LucideIcon,
} from "lucide-react";
import { api, isUnauthenticated } from "./lib/api";
import { SectionIcon } from "./components/ui";
import { lastQuery, watchUrl } from "./lib/persist";
import { LoginPage, SetupPage, SignupPage, signupInvite } from "./pages/Auth";
import { FleetPage } from "./pages/Fleet";
import { CatalogPage, RecipePage } from "./pages/Catalog";
import { ProjectPage, ProjectsPage } from "./pages/Projects";
import { RunPage } from "./pages/Run";
import { JobPage, JobsPage } from "./pages/Jobs";
import { DatasetPage, DatasetsPage } from "./pages/Datasets";
import { ModelsPage } from "./pages/Models";
import { OptimizationPage, OptimizationsPage } from "./pages/Optimize";
import { DeploymentsPage } from "./pages/Deployments";
import { Annotator, LabelingPage, LabelingProjectPage } from "./pages/Labeling";
import { NotebookPage, NotebooksPage } from "./pages/Notebooks";
import { NotificationBell } from "./pages/Notifications";
import { SettingsPage } from "./pages/Settings";
import { SweepPage, SweepsPage } from "./pages/Sweeps";
import { DeploymentMonitorPage } from "./pages/Monitoring";
import { PipelineRunPage, PipelinesPage } from "./pages/Pipelines";
import { DeploymentLlmPage } from "./pages/LlmOps";
import { FeaturesPage } from "./pages/Features";
import { IncidentBanner, LiveProvider, RunningTray, StatusPage } from "./pages/Ops";
import { OverviewPage } from "./pages/Overview";
import { AiModelsPage } from "./pages/AiModels";
import { AiActivityButton } from "./components/AiActivity";
import type { Me, WorkspaceMembership } from "./gen/mlplatform/v1/iam_pb";
import { setLang, translate as t, useT } from "./i18n";

/* App shell 30/01 §2 + routing 30/02: every page lives under /w/{workspace}/…; lifecycle sidebar; ⌘K command palette. */

/* Sidebar = 8 items in workflow order (data → label → train → model → deploy). Secondary pages are tabs inside their item; URLs are unchanged. */
type NavSub = { key: string; label: string; icon: LucideIcon };
type NavGroup = { label: string; icon: LucideIcon; subs: NavSub[] };
const GROUPS: NavGroup[] = [
  { label: "Overview", icon: LayoutDashboard, subs: [{ key: "", label: "Overview", icon: LayoutDashboard }] },
  { label: "Data", icon: Database, subs: [
    { key: "datasets", label: "Datasets", icon: Database },
    { key: "features", label: "Feature store", icon: Table2 }] },
  { label: "Labeling", icon: Tags, subs: [{ key: "labeling", label: "Labeling", icon: Tags }] },
  { label: "Training", icon: FlaskConical, subs: [
    { key: "projects", label: "Projects", icon: FlaskConical },
    { key: "jobs", label: "Jobs", icon: ListChecks },
    { key: "catalog", label: "Model Catalog", icon: LibraryBig },
    { key: "sweeps", label: "Sweeps (HPO)", icon: SlidersHorizontal },
    { key: "pipelines", label: "Pipelines", icon: Workflow },
    { key: "notebooks", label: "Notebooks", icon: NotebookPen }] },
  { label: "Models", icon: Boxes, subs: [
    { key: "models", label: "Models", icon: Boxes },
    { key: "optimize", label: "Optimization", icon: Gauge }] },
  { label: "Deployments", icon: Rocket, subs: [{ key: "deployments", label: "Deployments", icon: Rocket }] },
  { label: "Machines", icon: Server, subs: [
    { key: "fleet", label: "Machines", icon: Server },
    { key: "ai", label: "AI models", icon: BrainCircuit },
    { key: "status", label: "System status", icon: Activity }] },
  { label: "Settings", icon: Settings, subs: [{ key: "settings", label: "Settings", icon: Settings }] },
];
const groupOf = (key: string) => GROUPS.find((g) => g.subs.some((s) => s.key === key)) ?? GROUPS[0];
const subOf = (key: string) => groupOf(key).subs.find((s) => s.key === key) ?? GROUPS[0].subs[0];

/* ───────── theme ───────── */
function useTheme(): [string, () => void] {
  const [theme, setTheme] = useState(() => document.documentElement.dataset.theme ?? "dark");
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try { localStorage.setItem("mlp.theme", theme); } catch { /* browser storage blocked — keep going */ }
  }, [theme]);
  return [theme, () => setTheme(theme === "dark" ? "light" : "dark")];
}

/* ───────── auth + workspace selection ───────── */
function useMe() {
  const setup = useQuery({ queryKey: ["setup"], queryFn: () => api.instance.getSetupState({}) });
  const me = useQuery({ queryKey: ["me"], queryFn: () => api.iam.getMe({}), retry: (n, e) => !isUnauthenticated(e) && n < 2 });
  return { setup, me };
}

function Gate({ children }: { children: (me: Me) => ReactNode }) {
  const { setup, me } = useMe();
  if (setup.data?.setupRequired || window.location.pathname === "/setup") return <SetupPage />;
  if (me.isLoading || setup.isLoading) return <div className="flex h-screen items-center justify-center"><div className="skeleton h-8 w-48" /></div>;
  if (signupInvite() !== null) return <SignupPage signedInAs={me.data ? (me.data.displayName || me.data.email) : undefined} />;
  if (!me.data) return <LoginPage />;
  return <>{children(me.data)}</>;
}

const wsKey = (w: WorkspaceMembership) => w.slug || w.workspaceId;

/* ───────── khung ───────── */
function Shell({ me, ws, parts }: { me: Me; ws: WorkspaceMembership; parts: string[] }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [theme, toggleTheme] = useTheme();
  const tt = useT();   // re-render the shell when the language changes
  // Pages read ?tab=/?new= from the URL → re-render when only the query changes (same route).
  const searchStr = useRouterState({ select: (st) => st.location.searchStr });
  const [palette, setPalette] = useState(false);
  const [collapsed, setCollapsed] = useState(() => { try { return localStorage.getItem("mlp.sidebar") === "1"; } catch { return false; } });
  // Below lg the sidebar is a drawer over the page (closed by default, closes on navigation).
  const [drawer, setDrawer] = useState(false);
  const mini = collapsed && !drawer;
  const go = (p: string) => {
    const [path, query] = p.replace(/^\//, "").split("?");
    void navigate({ to: `/w/${wsKey(ws)}/${path}` as string, search: Object.fromEntries(new URLSearchParams(query ?? "")) as never });
  };
  // WebMCP: an in-browser AI agent gets the platform tools while the signed-in shell is mounted (unregistered on sign-out).
  const goRef = useRef(go);
  goRef.current = go;
  useEffect(() => {
    let undo = () => {};
    let gone = false;
    void registerWebMcp((p) => goRef.current(p)).then((u) => { if (gone) u(); else undo = u; });
    return () => { gone = true; undo(); };
  }, []);
  const section = parts[0] ?? "";
  // Remember the query (tab, filters, search) of each section's list page and restore it from the sidebar.
  useEffect(() => {
    if (parts.length > 1) return undefined;
    lastQuery.set(section, window.location.search);
    return watchUrl(() => { if (window.location.pathname.split("/").filter(Boolean).length <= 3) lastQuery.set(section, window.location.search); });
  }, [section, parts.length, searchStr]);
  const openSection = (key: string) => { const q = lastQuery.get(key); go(q ? `${key}?${q}` : key); };
  // A sidebar item reopens the tab last used inside it.
  useEffect(() => { try { localStorage.setItem(`mlp.navsub.${groupOf(section).label}`, section); } catch { /* ignore */ } }, [section]);
  const openGroup = (g: NavGroup) => {
    let k = g.subs[0].key;
    try { const saved = localStorage.getItem(`mlp.navsub.${g.label}`); if (saved !== null && g.subs.some((s) => s.key === saved)) k = saved; } catch { /* ignore */ }
    openSection(k);
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") { e.preventDefault(); setPalette((p) => !p); } };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);
  useEffect(() => { try { localStorage.setItem("mlp.sidebar", collapsed ? "1" : "0"); } catch { /* ignore */ } }, [collapsed]);

  const current = subOf(section);
  const currentGroup = groupOf(section);
  const crumbName = useCrumbNames();
  const crumbs = [...new Set([t(currentGroup.label), t(current.label)])].concat(
    parts.slice(1).filter((p) => !["runs", "annotate", "review", "llm"].includes(p)).map((p) => crumbName(p) ?? (p.length > 20 ? p.slice(0, 8) : p)));

  useEffect(() => { setDrawer(false); }, [section, parts.join("/")]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <LiveProvider key={ws.workspaceId} ws={ws.workspaceId}>
      <div className="flex min-h-screen">
        {drawer && <div className="fixed inset-0 z-40 bg-black/40 lg:hidden" onClick={() => setDrawer(false)} aria-hidden />}
        <aside className={`fixed inset-y-0 left-0 z-50 flex h-[100dvh] w-64 shrink-0 flex-col border-r border-line bg-surface transition-transform lg:sticky lg:top-0 lg:z-auto lg:h-screen lg:translate-x-0 lg:transition-[width] ${drawer ? "translate-x-0" : "-translate-x-full"} ${collapsed ? "lg:w-14" : "lg:w-60"}`}>
          <div className="flex items-center justify-between px-3 py-3">
            {!mini && <span className="text-[15px] font-semibold tracking-tight">ML<span className="text-primary">Platform</span></span>}
            <button className="inline-flex h-8 w-8 items-center justify-center rounded-md text-fg-subtle hover:bg-surface-2 hover:text-fg" onClick={() => setCollapsed(!collapsed)} aria-label={t("Collapse sidebar")}>
              {collapsed ? <PanelLeftOpen className="h-[18px] w-[18px]" /> : <PanelLeftClose className="h-[18px] w-[18px]" />}</button>
          </div>
          {!mini && (
            <button className="mx-3 mb-2 flex items-center gap-2 rounded-md border border-line bg-surface-2 px-2.5 py-2 text-sm text-fg-subtle hover:text-fg" onClick={() => setPalette(true)}>
              <Search className="h-4 w-4" /><span className="flex-1 text-left">{t("Search…")}</span><kbd className="mono rounded border border-line px-1 text-[11px]">⌘K</kbd>
            </button>)}
          <nav className="flex-1 space-y-0.5 overflow-y-auto px-2 pb-2">
            {GROUPS.map((g, i) => {
              const active = g === currentGroup;
              return (
                <div key={g.label}>
                  {(i === 6) && <div className="my-2 border-t border-line" />}
                  <button title={t(g.label)} onClick={() => openGroup(g)}
                    className={`flex w-full items-center gap-3 rounded-md px-2.5 py-2 text-left text-sm ${mini ? "justify-center" : ""} ${active ? "bg-primary/15 font-medium text-fg" : "text-fg-muted hover:bg-surface-2 hover:text-fg"}`}>
                    <g.icon className={`h-[18px] w-[18px] shrink-0 ${active ? "text-primary" : ""}`} strokeWidth={1.9} />
                    {!mini && <span className="flex-1 truncate">{t(g.label)}</span>}
                  </button>
                </div>);
            })}
          </nav>
          {!mini && <div className="border-t border-line px-2 py-2"><RunningTray go={go} /></div>}
          <div className="border-t border-line p-2">
            <Menu.Root>
              <Menu.Trigger className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-surface-2">
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded bg-primary/20 text-xs font-semibold text-primary">{ws.name.slice(0, 1).toUpperCase()}</span>
                {!mini && <span className="min-w-0 flex-1"><span className="block truncate">{ws.name}</span><span className="block text-[11px] text-fg-subtle">{ws.role}</span></span>}
                {!mini && <ChevronsUpDown className="h-4 w-4 text-fg-subtle" />}
              </Menu.Trigger>
              <Menu.Portal>
                <Menu.Content side="top" align="start" sideOffset={6} className="z-50 min-w-56 rounded-lg border border-line bg-overlay p-1 text-sm shadow-xl">
                  <Menu.Label className="px-2 py-1 text-[11px] text-fg-subtle">Workspace</Menu.Label>
                  {me.workspaces.map((w) => (
                    <Menu.Item key={w.workspaceId} className="flex cursor-pointer items-center justify-between rounded px-2 py-1.5 outline-none data-[highlighted]:bg-surface-2"
                      onSelect={() => void navigate({ to: `/w/${wsKey(w)}/${section}` as string })}>
                      {w.name}<span className="text-xs text-fg-subtle">{w.workspaceId === ws.workspaceId ? "✓" : w.role}</span>
                    </Menu.Item>))}
                </Menu.Content>
              </Menu.Portal>
            </Menu.Root>
          </div>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <header className="sticky top-0 z-30 flex h-12 items-center gap-1.5 border-b border-line bg-bg/85 px-3 backdrop-blur sm:gap-3 md:px-6">
            <button className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 lg:hidden" onClick={() => setDrawer(true)} aria-label={t("Menu")}>
              <Menu2 className="h-5 w-5" /></button>
            <nav className="flex min-w-0 flex-1 items-center gap-1.5 overflow-hidden whitespace-nowrap text-sm text-fg-muted">
              <span className="hidden shrink-0 text-fg-subtle md:inline">{ws.name}</span>
              {crumbs.map((c, i) => {
                const last = i === crumbs.length - 1;
                return (
                  <span key={i} className={`min-w-0 items-center gap-1.5 ${last ? "flex" : "hidden md:flex"}`}>
                    <ChevronRight className={`h-3.5 w-3.5 shrink-0 text-fg-subtle ${i === 0 ? "hidden md:block" : ""}`} />
                    <span className={`truncate ${last ? "text-fg" : "max-w-[10rem]"}`}>{c}</span></span>);
              })}
            </nav>
            <AiActivityButton key={"ai" + ws.workspaceId + tt.lang} ws={ws.workspaceId} isAdmin={me.isInstanceAdmin} go={go} />
            <NotificationBell key={ws.workspaceId + tt.lang} ws={ws.workspaceId} go={go} />
            <button className="inline-flex h-8 items-center gap-1.5 rounded-md px-2 text-xs font-medium text-fg-muted hover:bg-surface-2 hover:text-fg" onClick={() => setLang(tt.lang === "vi" ? "en" : "vi")}
              title={tt.lang === "vi" ? "English" : "Tiếng Việt"} aria-label={tt.lang === "vi" ? "English" : "Tiếng Việt"}><Languages className="h-4 w-4" />{tt.lang === "vi" ? "VI" : "EN"}</button>
            <button className="inline-flex h-8 w-8 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg" onClick={toggleTheme} title={t("Toggle theme")} aria-label={t("Toggle theme")}>
              {theme === "dark" ? <Moon className="h-4 w-4" /> : <Sun className="h-4 w-4" />}</button>
            <Menu.Root>
              <Menu.Trigger className="flex h-8 w-8 items-center justify-center rounded-full bg-surface-2 text-xs font-semibold text-fg hover:ring-2 hover:ring-primary/40">
                {me.displayName.slice(0, 1).toUpperCase()}
              </Menu.Trigger>
              <Menu.Portal>
                <Menu.Content align="end" sideOffset={6} className="z-50 min-w-52 rounded-lg border border-line bg-overlay p-1 text-sm shadow-xl">
                  <div className="px-2 py-1.5"><div className="font-medium">{me.displayName}</div><div className="text-xs text-fg-subtle">{me.email}{me.isInstanceAdmin ? " · Instance Admin" : ""}</div></div>
                  <Menu.Separator className="my-1 h-px bg-line" />
                  <Menu.Item className="cursor-pointer rounded px-2 py-1.5 outline-none data-[highlighted]:bg-surface-2" onSelect={() => go("settings?tab=profile")}>{t("Profile & password")}</Menu.Item>
                  <Menu.Item className="cursor-pointer rounded px-2 py-1.5 outline-none data-[highlighted]:bg-surface-2" onSelect={() => go("settings?tab=api-keys")}>API key</Menu.Item>
                  <Menu.Item className="cursor-pointer rounded px-2 py-1.5 outline-none data-[highlighted]:bg-surface-2" onSelect={() => go("settings?tab=notifications")}>{t("Notification preferences")}</Menu.Item>
                  <Menu.Item className="cursor-pointer rounded px-2 py-1.5 outline-none data-[highlighted]:bg-surface-2" onSelect={toggleTheme}>{theme === "dark" ? t("Light theme") : t("Dark theme")}</Menu.Item>
                  <Menu.Separator className="my-1 h-px bg-line" />
                  <Menu.Item className="cursor-pointer rounded px-2 py-1.5 text-danger outline-none data-[highlighted]:bg-surface-2"
                    onSelect={async () => { await api.iam.logout({}); await qc.invalidateQueries(); }}>{t("Sign out")}</Menu.Item>
                </Menu.Content>
              </Menu.Portal>
            </Menu.Root>
          </header>
          <main className="w-full min-w-0 flex-1 px-3 py-4 sm:px-4 md:px-6 md:py-6 2xl:px-10">
            <IncidentBanner go={go} />
            {currentGroup.subs.length > 1 && parts.length <= 1 && (
              <div className="-mt-2 mb-5 flex gap-1 overflow-x-auto border-b border-line" role="tablist">
                {currentGroup.subs.map((s) => {
                  const on = s.key === section;
                  return (
                    <button key={s.key} role="tab" aria-selected={on} onClick={() => openSection(s.key)} ref={on ? (el) => el?.scrollIntoView({ block: "nearest", inline: "nearest" }) : undefined}
                      className={`-mb-px flex shrink-0 items-center gap-1.5 border-b-2 px-3 py-2 text-sm ${on ? "border-primary font-medium text-fg" : "border-transparent text-fg-muted hover:text-fg"}`}>
                      <s.icon className="h-4 w-4" strokeWidth={1.9} />{t(s.label)}
                    </button>);
                })}
              </div>)}
            <SectionIcon.Provider value={current.icon}><Page key={tt.lang} ws={ws.workspaceId} parts={parts} go={go} me={me} search={searchStr} /></SectionIcon.Provider>
          </main>
        </div>
      </div>
      {palette && <Palette onClose={() => setPalette(false)} go={(p) => { setPalette(false); go(p); }} toggleTheme={toggleTheme} />}
    </LiveProvider>
  );
}

/* ⌘K: jump to page + quick commands */
function Palette({ onClose, go, toggleTheme }: { onClose: () => void; go: (p: string) => void; toggleTheme: () => void }) {
  return (
    <div className="fixed inset-0 z-[70] flex items-start justify-center bg-black/50 pt-[12vh]" onMouseDown={onClose}>
      <Command className="w-full max-w-xl overflow-hidden rounded-xl border border-line bg-overlay shadow-2xl" onMouseDown={(e) => e.stopPropagation()}
        onKeyDown={(e) => { if (e.key === "Escape") onClose(); }} label={t("Command palette")}>
        <Command.Input autoFocus placeholder={t("Search pages or commands…")} className="w-full border-b border-line bg-transparent px-4 py-3 text-sm outline-none placeholder:text-fg-subtle" />
        <Command.List className="max-h-80 overflow-y-auto p-2 text-sm">
          <Command.Empty className="px-3 py-6 text-center text-fg-subtle">{t("No results")}</Command.Empty>
          <Command.Group heading={t("Quick commands")} className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1 [&_[cmdk-group-heading]]:text-[11px] [&_[cmdk-group-heading]]:text-fg-subtle">
            {[["Invite member", "settings?tab=members"], ["Create API key", "settings?tab=api-keys"], ["Add a new machine", "fleet?new=1"], ["Update machine agents", "fleet?tab=agents"], ["Create notebook", "notebooks"], ["Train model", "projects"]].map(([l, p]) => (
              <Command.Item key={l} onSelect={() => go(p)} className="cursor-pointer rounded-md px-2 py-1.5 data-[selected=true]:bg-surface-2">→ {t(l)}</Command.Item>))}
            <Command.Item onSelect={() => { toggleTheme(); onClose(); }} className="cursor-pointer rounded-md px-2 py-1.5 data-[selected=true]:bg-surface-2">{t("◐ Toggle theme")}</Command.Item>
          </Command.Group>
          <Command.Group heading={t("Pages")} className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1 [&_[cmdk-group-heading]]:text-[11px] [&_[cmdk-group-heading]]:text-fg-subtle">
            {GROUPS.flatMap((g) => g.subs.map((n) => (
              <Command.Item key={n.key || "home"} value={`${n.label} ${t(n.label)}`} onSelect={() => go(n.key)} className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 data-[selected=true]:bg-surface-2">
                <n.icon className="h-4 w-4 text-fg-subtle" />{t(n.label)}{g.subs.length > 1 && <span className="ml-auto text-xs text-fg-subtle">{t(g.label)}</span>}
              </Command.Item>)))}
          </Command.Group>
        </Command.List>
      </Command>
    </div>
  );
}

function Redirect({ go, to }: { go: (p: string) => void; to: string }) {
  useEffect(() => { go(to); }, [go, to]);
  return null;
}

/* ───────── pages by path ───────── */
function Page({ ws, parts, go, me }: { ws: string; parts: string[]; go: (p: string) => void; me: Me; search?: string }) {
  const [section = "", a, b, c] = parts;
  switch (section) {
    case "": return <OverviewPage ws={ws} go={go} me={me} />;
    case "fleet": return <FleetPage ws={ws} go={go} />;
    case "models": return <ModelsPage ws={ws} go={go} />;
    case "optimize": return a ? <OptimizationPage ws={ws} id={a} go={go} onBack={() => go("optimize")} /> : <OptimizationsPage ws={ws} go={go} />;
    case "deployments":
      if (a && b === "llm") return <DeploymentLlmPage ws={ws} id={a} onBack={() => go("deployments")} />;
      return a ? <DeploymentMonitorPage ws={ws} id={a} onBack={() => go("deployments")} />
        : <DeploymentsPage ws={ws} onMonitor={(id) => go(`deployments/${id}`)} onLlm={(id) => go(`deployments/${id}/llm`)} />;
    case "status": return <StatusPage ws={ws} />;
    case "ai": return <AiModelsPage me={me} go={go} />;
    case "features": return <FeaturesPage ws={ws} />;
    case "llmops": return <Redirect go={go} to="deployments" />;
    case "pipelines": return a === "runs" && b ? <PipelineRunPage ws={ws} id={b} onBack={() => go("pipelines")} /> : <PipelinesPage ws={ws} onRun={(id) => go(`pipelines/runs/${id}`)} />;
    case "sweeps": return a ? <SweepPage ws={ws} id={a} onBack={() => go("sweeps")} /> : <SweepsPage ws={ws} onOpen={(id) => go(`sweeps/${id}`)} />;
    case "settings": return <SettingsPage ws={ws} me={me} go={go} />;
    case "notebooks": return a ? <NotebookPage ws={ws} id={a} onBack={() => go("notebooks")} /> : <NotebooksPage ws={ws} onOpen={(id) => go(`notebooks/${id}`)} />;
    case "labeling":
      if (a && (b === "annotate" || b === "review")) return <Annotator key={a + b} ws={ws} projectId={a} review={b === "review"} onExit={() => go(`labeling/${a}`)} />;
      return a ? <LabelingProjectPage ws={ws} id={a} go={go} onBack={() => go("labeling")} onWork={(m) => go(`labeling/${a}/${m}`)} /> : <LabelingPage ws={ws} go={go} onOpen={(id) => go(`labeling/${id}`)} onWork={(id, m) => go(`labeling/${id}/${m}`)} />;
    case "datasets": return a ? <DatasetPage ws={ws} id={a} onBack={() => go("datasets")} onOpenLabeling={(p) => go(`labeling/${p}`)} /> : <DatasetsPage ws={ws} onOpen={(id) => go(`datasets/${id}`)} />;
    case "jobs": return a ? <JobPage ws={ws} id={a} onBack={() => go("jobs")} /> : <JobsPage ws={ws} onOpen={(id) => go(`jobs/${id}`)} />;
    case "catalog": return a ? <RecipePage ws={ws} name={a} go={go} onBack={() => go("catalog")} /> : <CatalogPage ws={ws} go={go} onOpen={(n) => go(`catalog/${n}`)} />;
    case "projects":
      if (a && b === "runs" && c) return <RunPage key={c} ws={ws} id={c} onBack={() => go(`projects/${a}`)} go={go} />;
      return a ? <ProjectPage ws={ws} id={a} onBack={() => go("projects")} onRun={(r) => go(`projects/${a}/runs/${r}`)} go={go} /> : <ProjectsPage ws={ws} onOpen={(id) => go(`projects/${id}`)} />;
    default: return <div className="card py-16 text-center text-fg-muted">{t("Page not found.")}</div>;
  }
}

function WorkspaceRoute() {
  const { slug, _splat } = useParams({ strict: false }) as { slug: string; _splat?: string };
  return (
    <Gate>{(me) => {
      const ws = me.workspaces.find((w) => wsKey(w) === slug || w.workspaceId === slug);
      if (!ws) return me.workspaces[0] ? <Navigate to={`/w/${wsKey(me.workspaces[0])}` as string} /> : <div className="p-8">{t("You are not in any workspace.")}</div>;
      const parts = (_splat ?? "").split("?")[0].split("/").filter(Boolean);
      return <Shell me={me} ws={ws} parts={parts} />;
    }}</Gate>
  );
}

function HomeRoute() {
  // Legacy #/projects/… links → /w/{ws}/projects/…
  const hash = window.location.hash.replace(/^#\/?/, "");
  const legacy = hash.startsWith("signup") ? "" : hash;
  return <Gate>{(me) => me.workspaces[0] ? <Navigate to={`/w/${wsKey(me.workspaces[0])}/${legacy}` as string} replace /> : <div className="p-8">{t("You are not in any workspace.")}</div>}</Gate>;
}

const rootRoute = createRootRoute({ component: () => <Outlet /> });
const routeTree = rootRoute.addChildren([
  createRoute({ getParentRoute: () => rootRoute, path: "/", component: HomeRoute }),
  createRoute({ getParentRoute: () => rootRoute, path: "/setup", component: () => <SetupPage /> }),
  createRoute({ getParentRoute: () => rootRoute, path: "/signup", component: HomeRoute }),
  createRoute({ getParentRoute: () => rootRoute, path: "/w/$slug", component: WorkspaceRoute }),
  createRoute({ getParentRoute: () => rootRoute, path: "/w/$slug/$", component: WorkspaceRoute }),
]);

export function App() {
  const router = useMemo(() => createRouter({
    routeTree, defaultPreload: false,
    // Plain query strings (?tab=audit&new=1) — TanStack's default JSON encoding (?tab="audit") breaks page parsing, so tabs/dialogs don't work.
    parseSearch: (q) => Object.fromEntries(new URLSearchParams(q)),
    stringifySearch: (o) => {
      const q = new URLSearchParams(Object.entries(o).filter(([, v]) => v !== undefined && v !== null && v !== "").map(([k, v]) => [k, String(v)])).toString();
      return q ? `?${q}` : "";
    },
  }), []);
  return <RouterProvider router={router} />;
}
