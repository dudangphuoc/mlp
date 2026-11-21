import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { useState } from "react";
import type { JobProgress } from "../gen/mlplatform/v1/scheduler_pb";
import { api, errorText } from "../lib/api";
import { ago, bytes, day } from "../lib/format";
import { translate as t } from "../i18n";
import { ConfirmDialog, ErrorBox } from "./ui";

/** Live `sync` detail the shim reports while it fills the host dataset cache (shim DataSync.cs). */
type Sync = {
  dataset?: string; files: number; reused_files: number; reused_bytes: number; fetch_files: number; fetch_bytes: number;
  done_files: number; done_bytes: number; uncached_files: number; retries: number; seconds: number; free_bytes: number; final: boolean;
  failed: { path: string; error: string; attempt: number }[];
};

function useNames(ws: string) {
  const ds = useQuery({ queryKey: ["datasets", ws, ""], queryFn: () => api.data.listDatasets({ workspaceId: ws, task: "" }), staleTime: 60_000 });
  const hosts = useQuery({ queryKey: ["hosts", ws], queryFn: () => api.fleet.listHosts({ workspaceId: ws }), staleTime: 30_000 });
  return {
    dataset: (id: string) => ds.data?.datasets.find((d) => d.id === id)?.name ?? id.slice(0, 8),
    host: (id: string) => hosts.data?.hosts.find((h) => h.id === id)?.name ?? id.slice(0, 8),
  };
}

function Chip({ tone, children }: { tone: "ok" | "info" | "muted" | "bad"; children: React.ReactNode }) {
  const c = { ok: "bg-emerald-500/15 text-emerald-600", info: "bg-primary/15 text-primary", muted: "bg-surface-2 text-fg-muted", bad: "bg-danger/15 text-danger" }[tone];
  return <span className={`rounded-md px-2.5 py-1 tabular-nums ${c}`}>{children}</span>;
}

const eta = (s: number) => (s >= 3600 ? `${Math.floor(s / 3600)} h ${Math.round((s % 3600) / 60)} m` : s >= 60 ? `${Math.floor(s / 60)} m ${Math.round(s % 60)} s` : `${Math.round(s)} s`);

/** Job page: what the dataset transfer did (live while preparing data, then the stored summary of every attempt). */
export function DataSyncPanel({ ws, jobId, progress, live }: { ws: string; jobId: string; progress?: JobProgress; live: boolean }) {
  const names = useNames(ws);
  const history = useQuery({ queryKey: ["job-data-syncs", ws, jobId], queryFn: () => api.fleet.listJobDataSyncs({ workspaceId: ws, jobId }), refetchInterval: live ? 4000 : false });
  let sync: Sync | null = null;
  if (progress?.phase === "preparing_data" && progress.detailJson) {
    try { sync = (JSON.parse(progress.detailJson) as { sync?: Sync }).sync ?? null; } catch { sync = null; }
  }
  const rows = history.data?.syncs ?? [];
  if (!(live && sync && !sync.final) && rows.length === 0) return null;
  return (
    <div className="card mb-4 space-y-3 text-sm">
      {live && sync && !sync.final && (
        <>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="font-medium">{t("Data")} · {sync.dataset ? names.dataset(sync.dataset) : ""}</span>
            <span className="font-mono text-xs text-fg-muted">{t("free")} {bytes(sync.free_bytes)}</span>
          </div>
          <div className="flex flex-wrap gap-2 text-xs">
            <Chip tone="ok">{t("On the machine")} {sync.reused_files.toLocaleString()} · {bytes(sync.reused_bytes)}</Chip>
            <Chip tone="info">{t("To download")} {sync.fetch_files.toLocaleString()} · {bytes(sync.fetch_bytes)}</Chip>
            {sync.uncached_files > 0 && <Chip tone="muted">{t("Not cacheable")} {sync.uncached_files.toLocaleString()}</Chip>}
            {sync.failed.length > 0 && <Chip tone="bad">{t("Retrying")} {sync.failed.length}</Chip>}
          </div>
          {sync.fetch_files > 0 && (
            <div>
              <div className="mb-1 flex flex-wrap justify-between gap-2 text-xs text-fg-muted tabular-nums">
                <span>{sync.done_files.toLocaleString()} / {sync.fetch_files.toLocaleString()} · {bytes(sync.done_bytes)} / {bytes(sync.fetch_bytes)}</span>
                <span>{progress!.rate > 0 ? `${bytes(progress!.rate)}/s` : "—"}{progress!.etaSeconds > 0 ? ` · ${t("left")} ${eta(progress!.etaSeconds)}` : ""}</span>
              </div>
              <div className="h-2 overflow-hidden rounded bg-surface-2">
                <div className="h-full bg-primary transition-all" style={{ width: `${sync.fetch_bytes > 0 ? Math.min(100, (sync.done_bytes / sync.fetch_bytes) * 100) : 0}%` }} />
              </div>
            </div>)}
          {sync.failed.map((f) => (
            <div key={f.path} className="border-t border-line pt-2 font-mono text-xs text-danger">{f.path} · {f.error} · {t("attempt")} {f.attempt}/10</div>))}
        </>)}
      {rows.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full whitespace-nowrap text-xs [&_td]:px-2 [&_td]:py-1.5 [&_th]:px-2 [&_th]:py-1.5">
            <thead className="text-left text-fg-muted"><tr>
              <th>{t("Data")}</th><th>{t("Machine")}</th><th>{t("Files")}</th><th>{t("On the machine")}</th><th>{t("Downloaded")}</th><th>{t("Time")}</th><th>{t("Retries")}</th><th />
            </tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id} className="border-t border-line tabular-nums">
                <td>{names.dataset(r.datasetId)}</td><td>{names.host(r.hostId)}</td><td>{r.files.toLocaleString()}</td>
                <td className="text-emerald-600">{r.reusedFiles.toLocaleString()} · {bytes(r.reusedBytes)}</td>
                <td className={r.fetchedFiles > 0 ? "text-primary" : "text-fg-muted"}>{r.fetchedFiles.toLocaleString()} · {bytes(r.fetchedBytes)}</td>
                <td>{r.seconds.toFixed(1)} s</td><td>{r.retries}</td><td className="text-fg-subtle">{r.at ? ago(r.at) : ""}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
    </div>
  );
}

/** Machines page: dataset caches each machine holds, with delete (forces a full download next time). */
export function DataCacheList({ ws, go }: { ws: string; go: (p: string) => void }) {
  const qc = useQueryClient();
  const names = useNames(ws);
  const caches = useQuery({ queryKey: ["data-caches", ws], queryFn: () => api.fleet.listDataCaches({ workspaceId: ws, hostId: "" }), refetchInterval: 10_000 });
  const [del, setDel] = useState<{ hostId: string; datasetId: string } | null>(null);
  const remove = useMutation({
    mutationFn: (v: { hostId: string; datasetId: string }) => api.fleet.deleteDataCache({ workspaceId: ws, ...v }),
    onSuccess: () => { setDel(null); void qc.invalidateQueries({ queryKey: ["data-caches", ws] }); },
  });
  const rows = caches.data?.caches ?? [];
  if (caches.isLoading) return <div className="skeleton h-40" />;
  return (
    <>
      <ErrorBox error={caches.error ? errorText(caches.error) : remove.error && !del ? errorText(remove.error) : null} />
      {rows.length === 0 ? <div className="card text-sm text-fg-muted">—</div> : (
        <div className="card overflow-x-auto p-0">
          <table className="w-full whitespace-nowrap text-sm [&_td]:px-3 [&_td]:py-2.5 [&_th]:px-3 [&_th]:py-2.5">
            <thead className="text-left text-xs text-fg-muted"><tr>
              <th>{t("Machine")}</th><th>{t("Dataset")}</th><th>{t("Files")}</th><th>{t("Size")}</th><th>{t("Disk free")}</th><th>{t("Last used")}</th><th>{t("Auto delete")}</th><th>{t("Status")}</th><th />
            </tr></thead>
            <tbody>{rows.map((c) => (
              <tr key={c.hostId + c.datasetId} className="border-t border-line tabular-nums">
                <td>{names.host(c.hostId)}</td><td className="font-medium">{names.dataset(c.datasetId)}</td>
                <td>{Number(c.cacheFiles).toLocaleString()}</td><td>{bytes(c.cacheBytes)}</td><td>{bytes(c.freeBytes)}</td>
                <td><button className="text-primary hover:underline" onClick={() => go(`jobs/${c.lastJobId}`)}>{c.lastUsedAt ? ago(c.lastUsedAt) : "—"}</button></td>
                <td className="text-fg-muted">{c.inUse || !c.lastUsedAt ? "—" : day((Number(c.lastUsedAt.seconds) + 14 * 86400) * 1000)}</td>
                <td>{c.inUse ?<span className="rounded-md bg-primary/15 px-2 py-0.5 text-xs text-primary">{t("In use")}</span> : <span className="text-xs text-fg-muted">{t("Idle")}</span>}</td>
                <td className="text-right">
                  <button className="btn-ghost h-7 px-2 text-xs text-danger" disabled={c.inUse} aria-label={t("Delete")} onClick={() => { remove.reset(); setDel({ hostId: c.hostId, datasetId: c.datasetId }); }}>
                    <Trash2 className="h-3.5 w-3.5" /></button>
                </td>
              </tr>))}</tbody>
          </table>
        </div>)}
      {del && (
        <ConfirmDialog title={t("Delete data on {host}", { host: names.host(del.hostId) })} label={t("Delete")} busy={remove.isPending}
          consequence={<>
            <div>{names.dataset(del.datasetId)}</div>
            <ErrorBox error={remove.error ? errorText(remove.error) : null} />
          </>}
          onConfirm={() => remove.mutate(del)} onClose={() => setDel(null)} />)}
    </>
  );
}

/** Project canvas, data node: before a run, how much of the version is already on the machine and how much would be downloaded. */
export function TransferEstimate({ ws, versionId, hostId }: { ws: string; versionId: string; hostId: string }) {
  const names = useNames(ws);
  const est = useQuery({ queryKey: ["data-estimate", ws, versionId], enabled: !!versionId, staleTime: 30_000,
    queryFn: () => api.fleet.estimateDataTransfer({ workspaceId: ws, datasetVersionId: versionId }) });
  if (!est.data) return null;
  const e = est.data;
  const h = hostId ? e.hosts.find((x) => x.hostId === hostId) : e.hosts[0];
  const cachedFiles = h?.known ? Number(h.cachedFiles) : 0, cachedBytes = h?.known ? Number(h.cachedBytes) : 0;
  const needBytes = Number(e.bytes) - cachedBytes;
  const short = h && h.freeBytes > 0n && needBytes > Number(h.freeBytes);
  return (
    <div className="flex flex-wrap items-center gap-1.5 tabular-nums">
      {cachedFiles > 0 && <span className="rounded bg-emerald-500/15 px-1.5 py-0.5 text-emerald-600">{t("On the machine")} {bytes(cachedBytes)}</span>}
      <span className={`rounded px-1.5 py-0.5 ${short ? "bg-danger/15 text-danger" : needBytes > 0 ? "bg-primary/15 text-primary" : "bg-surface-2 text-fg-muted"}`}>
        {t("To download")} {bytes(needBytes)}{cachedFiles > 0 && Number(e.files) > cachedFiles ? ` · ${(Number(e.files) - cachedFiles).toLocaleString()}` : ""}</span>
      {!hostId && h && cachedFiles > 0 && <span className="text-fg-subtle">{names.host(h.hostId)}</span>}
    </div>
  );
}
