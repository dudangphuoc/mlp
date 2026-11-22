import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, errorText } from "../../lib/api";
import { GpuState, type GpuSample } from "../../gen/mlplatform/v1/agent_pb";
import type { GpuCard, HostGpuView } from "../../gen/mlplatform/v1/fleet_pb";
import { bytes } from "../../lib/format";
import { EmptyState, ErrorBox, RelTime, Skeleton, Tabs } from "../ui";
import { GpuHistoryChart } from "./GpuHistoryChart";
import { translate as t } from "../../i18n";

type Range = "1h" | "24h" | "7d" | "30d";
const LEVEL: Record<string, string> = { crit: "bg-danger/15 text-danger", warn: "bg-warning/15 text-warning", info: "bg-sky-100 text-sky-700" };

function Bar({ value, max = 100, tone = "bg-accent", marker }: { value: number; max?: number; tone?: string; marker?: number }) {
  const pct = Math.max(0, Math.min(100, (value / (max || 1)) * 100));
  return (
    <div className="relative h-2 w-full overflow-hidden rounded-full bg-surface-2">
      <div className={`h-full ${tone}`} style={{ width: `${pct}%` }} />
      {marker !== undefined && <div className="absolute inset-y-0 w-0.5 bg-fg-subtle" style={{ left: `${Math.min(100, (marker / (max || 1)) * 100)}%` }} />}
    </div>
  );
}

function tempTone(s: GpuSample) {
  const limit = s.temperatureLimitC ?? 92;
  return s.temperatureC >= limit ? "bg-danger" : s.temperatureC >= limit - 5 ? "bg-warning" : "bg-success";
}

function Row({ label, value, children }: { label: string; value: string; children?: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <div className="flex items-baseline justify-between text-xs"><span className="text-fg-muted">{label}</span><span className="tabular-nums">{value}</span></div>
      {children}
    </div>
  );
}

function Card({ c, selected, onSelect }: { c: GpuCard; selected: boolean; onSelect: () => void }) {
  const s = c.sample!;
  const total = Number(s.memoryTotalBytes || c.info?.memoryBytes || 0n);
  const used = Number(s.memoryUsedBytes);
  return (
    <button type="button" onClick={onSelect} className={`card space-y-2.5 text-left ${selected ? "ring-2 ring-accent" : ""}`}>
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0"><div className="truncate font-medium">GPU {c.info?.index} · {c.info?.name}</div>
          <div className="mono truncate text-[11px] text-fg-subtle">{s.pstate || "—"} · {c.info?.computeCapability || "—"}</div></div>
        <div className="flex flex-wrap justify-end gap-1">{c.alerts.map((a) => <span key={a.kind} className={`badge ${LEVEL[a.level] ?? ""}`}>{t(a.kind)}</span>)}</div>
      </div>
      <Row label={t("Utilisation")} value={`${s.utilPercent.toFixed(0)}%`}><Bar value={s.utilPercent} /></Row>
      <Row label="VRAM" value={`${bytes(used)} / ${bytes(total)}`}><Bar value={used} max={total} tone="bg-violet-400" marker={Number(c.reservedMemoryBytes)} /></Row>
      <Row label={t("Temperature")} value={`${s.temperatureC.toFixed(0)} °C${s.temperatureLimitC !== undefined ? ` / ${s.temperatureLimitC.toFixed(0)}` : ""}`}>
        <Bar value={s.temperatureC} max={s.temperatureLimitC ?? 100} tone={tempTone(s)} /></Row>
      <Row label={t("Power")} value={`${s.powerW.toFixed(0)} W${s.powerLimitW !== undefined ? ` / ${s.powerLimitW.toFixed(0)}` : ""}`}>
        {s.powerLimitW !== undefined && <Bar value={s.powerW} max={s.powerLimitW} tone="bg-amber-400" />}</Row>
      <div className="grid grid-cols-2 gap-x-3 gap-y-1 border-t border-line pt-2 text-xs">
        <span className="text-fg-muted">SM</span><span className="text-right tabular-nums">{s.smClockMhz !== undefined ? `${s.smClockMhz.toFixed(0)} / ${s.smClockMaxMhz?.toFixed(0) ?? "—"} MHz` : "—"}</span>
        <span className="text-fg-muted">{t("Fan")}</span><span className="text-right tabular-nums">{s.fanPercent !== undefined ? `${s.fanPercent.toFixed(0)}%` : "—"}</span>
        <span className="text-fg-muted">{t("Free to schedule")}</span><span className="text-right tabular-nums">{bytes(c.schedulableMemoryBytes)}</span>
        <span className="text-fg-muted">{t("Throttle")}</span><span className="truncate text-right">{s.throttleReasons.length ? s.throttleReasons.join(", ") : "—"}</span>
      </div>
    </button>
  );
}

function Usage({ v }: { v: HostGpuView }) {
  if (v.usage.length === 0) return null;
  const index = new Map(v.gpus.map((g) => [g.info?.uuid, g.info?.index]));
  return (
    <div className="card overflow-x-auto p-0">
      <table className="w-full text-sm">
        <thead className="text-left text-xs text-fg-subtle"><tr><th className="px-3 py-2">GPU</th><th className="px-3 py-2">{t("Container")}</th><th className="px-3 py-2">{t("Job")}</th><th className="px-3 py-2">{t("Environment")}</th><th className="px-3 py-2 text-right">VRAM</th></tr></thead>
        <tbody>{v.usage.map((u, i) => (
          <tr key={i} className="border-t border-line">
            <td className="px-3 py-2 tabular-nums">{index.get(u.gpuUuid) ?? "—"}</td>
            <td className="mono px-3 py-2 text-xs">{u.container || "—"}</td>
            <td className="px-3 py-2">{u.jobName || "—"}</td>
            <td className="px-3 py-2"><span className={`badge ${u.environment === "other" ? "bg-warning/15 text-warning" : "bg-surface-2"}`}>{t(u.environment)}</span></td>
            <td className="px-3 py-2 text-right tabular-nums">{bytes(u.usedMemoryBytes)}{u.estimated ? " ≈" : ""}</td>
          </tr>))}</tbody>
      </table>
    </div>
  );
}

function History({ ws, hostId, uuid }: { ws: string; hostId: string; uuid: string }) {
  const [range, setRange] = useState<Range>("1h");
  const q = useQuery({ queryKey: ["gpu-history", ws, hostId, uuid, range], queryFn: () => api.fleet.getGpuHistory({ workspaceId: ws, hostId, gpuUuid: uuid, range }), refetchInterval: 15000 });
  return (
    <div className="card space-y-2">
      <Tabs<Range> value={range} onChange={setRange} tabs={[{ key: "1h", label: "1h" }, { key: "24h", label: "24h" }, { key: "7d", label: "7d" }, { key: "30d", label: "30d" }]} />
      {q.data ? (q.data.points.length ? <GpuHistoryChart h={q.data} /> : <EmptyState bare title={t("No samples yet")} />) : <Skeleton rows={4} />}
    </div>
  );
}

/** Per-GPU readings of one machine: cards, who uses which GPU, history. */
export function GpuPanel({ ws, hostId, compact }: { ws: string; hostId: string; compact?: boolean }) {
  const q = useQuery({ queryKey: ["host-gpus", ws, hostId], queryFn: () => api.fleet.getHostGpus({ workspaceId: ws, hostId }), refetchInterval: 15000 });
  const [picked, setPicked] = useState<string | null>(null);
  const v = q.data;
  if (q.error) return <ErrorBox error={errorText(q.error)} onRetry={() => void q.refetch()} />;
  if (!v) return <Skeleton rows={5} />;
  if (v.state === GpuState.NONE || v.gpus.length === 0) return <EmptyState bare title={t("no GPU")} />;
  const selected = picked ?? v.gpus[0]?.info?.uuid ?? "";
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
        {v.state === GpuState.DRIVER_ERROR && <span className="badge bg-danger/15 text-danger">{t("driver error")}{v.error ? ` · ${v.error}` : ""}</span>}
        {v.gpuPriority && <span className="badge bg-surface-2">{t("GPU priority")}: {t(v.gpuPriority)}</span>}
        {v.yield?.active && <span className="badge bg-warning/15 text-warning">{t("yielded")} · {v.yield.foreignContainers.join(", ")}</span>}
        <span>{t("sampled")} <RelTime at={v.sampledAt} /></span>
      </div>
      <div className={`grid gap-3 ${compact ? "sm:grid-cols-2" : "md:grid-cols-2 xl:grid-cols-3"}`}>
        {v.gpus.map((c) => <Card key={c.info?.uuid} c={c} selected={c.info?.uuid === selected} onSelect={() => setPicked(c.info?.uuid ?? null)} />)}
      </div>
      <Usage v={v} />
      {selected && <History ws={ws} hostId={hostId} uuid={selected} />}
    </div>
  );
}
