import type { Host } from "../../gen/mlplatform/v1/fleet_pb";
import { EmptyState } from "../ui";
import { GpuPanel } from "./GpuPanel";
import { translate as t } from "../../i18n";

/** Every GPU of every machine with GPUs, one section per machine. */
export function FleetGpuView({ ws, hosts, onOpen }: { ws: string; hosts: Host[]; onOpen: (h: Host) => void }) {
  const withGpu = hosts.filter((h) => (h.hostInfo?.gpus.length ?? 0) > 0);
  if (withGpu.length === 0) return <EmptyState title={t("no GPU")} />;
  return (
    <div className="space-y-6">
      {withGpu.map((h) => (
        <section key={h.id} className="space-y-2">
          <button className="text-sm font-medium hover:underline" onClick={() => onOpen(h)}>{h.name}</button>
          <GpuPanel ws={ws} hostId={h.id} compact />
        </section>))}
    </div>
  );
}
