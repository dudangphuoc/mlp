import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/api";
import type { Me } from "../gen/mlplatform/v1/iam_pb";
import { EmptyState, PageHeader, RelTime, StatCard, StateBadge } from "../components/ui";
import { useLive } from "./Ops";
import { translate as t } from "../i18n";
import { CircleCheck, CircleX, RefreshCw, Server, TriangleAlert } from "lucide-react";

/** S-00-1 Overview (30/02 §3): linked stat tiles, my work, machines, recent activity. */
export function OverviewPage({ ws, go, me }: { ws: string; go: (p: string) => void; me: Me }) {
  const { jobs, system } = useLive();
  const hosts = useQuery({ queryKey: ["hosts", ws], queryFn: () => api.fleet.listHosts({ workspaceId: ws }), refetchInterval: 10000 });
  const deps = useQuery({ queryKey: ["deployments", ws], queryFn: () => api.serving.listDeployments({ workspaceId: ws }), refetchInterval: 15000 });
  const nbs = useQuery({ queryKey: ["notebooks", ws], queryFn: () => api.notebook.listServers({ workspaceId: ws }), refetchInterval: 15000 });
  const recent = useQuery({ queryKey: ["jobs-recent", ws], queryFn: () => api.scheduler.listJobs({ workspaceId: ws }), refetchInterval: 15000 });

  const jobsAll = recent.data?.jobs ?? [];
  const hl = hosts.data?.hosts ?? [];
  const online = hl.filter((h) => h.state === "online" || h.state === "busy");
  const gpuTotal = hl.reduce((n, h) => n + (h.hostInfo?.gpus.length ?? 0), 0);
  const gpuOnline = online.reduce((n, h) => n + (h.hostInfo?.gpus.length ?? 0), 0);
  const running = jobs.filter((j) => j.state === "running").length;
  const waiting = jobs.filter((j) => ["queued", "scheduling", "starting"].includes(j.state)).length;
  const dl = deps.data?.deployments ?? [];
  const liveDeps = dl.filter((d) => d.state !== "stopped");   // stopped deployments are history, not something unhealthy
  const healthy = liveDeps.filter((d) => d.state === "healthy" || d.state === "ready").length;
  const openNb = (nbs.data?.servers ?? []).filter((n) => n.state === "running").length;
  const alerts = jobs.filter((j) => j.alert);
  // Jobs failed in the last 24 hours, grouped by name (latest only + count) — avoids repeating the same long error.
  const dayAgo = Date.now() / 1000 - 86400;
  const failedMap = new Map<string, { job: (typeof jobsAll)[number]; count: number }>();
  for (const j of jobsAll.filter((j) => j.state === "failed" && Number(j.createdAt?.seconds ?? 0) > dayAgo)) {
    const cur = failedMap.get(j.name);
    if (cur) cur.count++; else failedMap.set(j.name, { job: j, count: 1 });
  }
  const failed = [...failedMap.values()].slice(0, 5);
  const outdated = hl.filter((h) => h.updateAvailable).length;
  const sysDown = system?.items.filter((i) => i.status !== "ok") ?? [];

  return (
    <>
      <PageHeader title={t("Hello, {name}", { name: me.displayName })}
        actions={<>
          <button className="btn-ghost" onClick={() => go("datasets")}>+ Dataset</button>
          <button className="btn-ghost" onClick={() => go("notebooks")}>+ Notebook</button>
          <button className="btn-primary" onClick={() => go("projects")}>+ Train</button>
        </>} />
      <div className="mb-6 grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
        <StatCard label={t("Machines online")} value={`${online.length} / ${hl.length}`} onClick={() => go("fleet")} tone={hl.length && online.length < hl.length ? "warning" : undefined} />
        <StatCard label={t("GPUs ready / total")} value={`${gpuOnline} / ${gpuTotal}`} onClick={() => go("fleet?tab=map")} />
        <StatCard label={t("Jobs running / waiting")} value={`${running} / ${waiting}`} onClick={() => go("jobs")} tone={alerts.length ? "danger" : undefined} sub={alerts.length ? t("{n} need attention", { n: alerts.length }) : undefined} />
        <StatCard label={t("Open notebooks")} value={openNb} onClick={() => go("notebooks")} />
        <StatCard label={t("Healthy deployments")} value={`${healthy} / ${liveDeps.length}`} onClick={() => go("deployments")} tone={liveDeps.length && healthy < liveDeps.length ? "warning" : undefined} />
        <StatCard label={t("System")} value={sysDown.length ? t("{n} incidents", { n: sysDown.length }) : t("All normal")} onClick={() => go("status")} tone={sysDown.length ? "danger" : "success"} />
      </div>

      <div className="mb-4 grid gap-4 lg:grid-cols-3">
        <section className="card flex max-h-[360px] min-h-[220px] flex-col lg:col-span-2">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-semibold">{t("Needs attention")}</h2>
            {(alerts.length + failed.length + (outdated ? 1 : 0)) > 0 && <span className="badge bg-surface-2 text-fg-muted">{alerts.length + failed.length + (outdated ? 1 : 0)}</span>}
          </div>
          {alerts.length + failed.length + (outdated ? 1 : 0) === 0 ? (
            <div className="flex flex-1 flex-col items-center justify-center gap-2 text-sm text-fg-subtle"><CircleCheck className="h-6 w-6 text-success" />{t("Nothing needs your attention.")}</div>
          ) : (
            <ul className="-mx-2 min-h-0 flex-1 space-y-0.5 overflow-y-auto text-sm">
              {alerts.map((j) => (
                <li key={j.id}><button className="flex w-full items-start gap-2.5 rounded-md px-2 py-2 text-left hover:bg-surface-2" onClick={() => go(`jobs/${j.id}`)}>
                  <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-danger" />
                  <span className="min-w-0 flex-1"><span className="block truncate font-medium">{j.name}</span><span className="line-clamp-2 text-xs text-fg-muted">{t(j.alertText)}</span></span>
                </button></li>))}
              {failed.map(({ job: j, count }) => (
                <li key={j.id}><button className="flex w-full items-start gap-2.5 rounded-md px-2 py-2 text-left hover:bg-surface-2" onClick={() => go(`jobs/${j.id}`)}>
                  <CircleX className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
                  <span className="min-w-0 flex-1">
                    <span className="flex items-center gap-2"><span className="truncate font-medium">{j.name}</span>
                      <span className="shrink-0 text-xs text-fg-subtle">{t("failed")}{count > 1 ? ` \u00d7${count}` : ""} · <RelTime at={j.createdAt} /></span></span>
                    {j.stateReason && <span className="line-clamp-2 break-all text-xs text-fg-muted">{t(j.stateReason)}</span>}
                  </span>
                </button></li>))}
              {outdated > 0 && <li><button className="flex w-full items-start gap-2.5 rounded-md px-2 py-2 text-left hover:bg-surface-2" onClick={() => go("fleet")}>
                <RefreshCw className="mt-0.5 h-4 w-4 shrink-0 text-info" /><span>{t("{n} machines have a new agent — updating does not stop running jobs", { n: outdated })}</span>
              </button></li>}
            </ul>)}
        </section>

        <section className="card flex max-h-[360px] min-h-[220px] flex-col">
          <div className="mb-3 flex items-center justify-between"><h2 className="text-sm font-semibold">{t("Machines")}</h2>
            <button className="text-xs text-primary hover:underline" onClick={() => go("fleet")}>{t("View all →")}</button></div>
          {hl.length === 0 ? <div className="flex flex-1 items-center justify-center"><EmptyState bare icon={<Server className="h-6 w-6" strokeWidth={1.7} />} title={t("No machines yet")} action={<button className="btn-primary" onClick={() => go("fleet?new=1")}>{t("Add machine")}</button>} /></div> : (
            <ul className="-mx-2 min-h-0 flex-1 space-y-0.5 overflow-y-auto">
              {hl.map((h) => (
                <li key={h.id}><button className="w-full rounded-md px-2 py-2 text-left hover:bg-surface-2" onClick={() => go("fleet")}>
                  <span className="flex items-center gap-2 text-sm"><StateBadge state={h.state} /><span className="min-w-0 flex-1 truncate font-medium">{h.name}</span></span>
                  <span className="mt-1 flex items-center justify-between gap-2 text-xs text-fg-subtle">
                    <span className="truncate">{h.hostInfo?.gpus[0]?.name ?? t("no GPU")}</span><RelTime at={h.lastSeenAt} /></span>
                </button></li>))}
            </ul>)}
        </section>
      </div>

      <section className="card">
        <div className="mb-3 flex items-center justify-between"><h2 className="text-sm font-semibold">{t("Recent jobs")}</h2>
          <button className="text-xs text-primary hover:underline" onClick={() => go("jobs")}>{t("View all →")}</button></div>
        {jobsAll.length === 0 ? <div className="py-8 text-center text-sm text-fg-subtle">{t("No jobs yet.")}</div> : (
          <table className="w-full table-fixed text-sm">
            <thead><tr className="text-left text-xs text-fg-subtle">
              <th className="w-[34%] pb-2 font-medium">{t("Name")}</th><th className="w-[14%] pb-2 font-medium">{t("Kind")}</th>
              <th className="pb-2 font-medium">{t("Status")}</th><th className="w-[14%] pb-2 text-right font-medium">{t("Created")}</th></tr></thead>
            <tbody className="divide-y divide-line">
              {jobsAll.slice(0, 8).map((j) => (
                <tr key={j.id} className="cursor-pointer hover:bg-surface-2" onClick={() => go(`jobs/${j.id}`)}>
                  <td className="truncate py-2 pr-3 font-medium">{j.name}</td><td className="py-2 text-fg-muted">{j.kind}</td>
                  <td className="py-2"><StateBadge state={j.state} reason={j.stateReason || undefined} /></td>
                  <td className="py-2 text-right text-xs text-fg-subtle"><RelTime at={j.createdAt} /></td>
                </tr>))}
            </tbody>
          </table>)}
      </section>
    </>
  );
}
