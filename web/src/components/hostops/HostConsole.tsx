import { useState } from "react";
import type { Host } from "../../gen/mlplatform/v1/fleet_pb";
import { Dialog, Tabs } from "../ui";
import { GpuPanel } from "./GpuPanel";
import { TerminalPanel } from "./TerminalPanel";
import { FilesPanel } from "./FilesPanel";
import { ProcessesPanel } from "./ProcessesPanel";
import { ServicesPanel } from "./ServicesPanel";
import { translate as t } from "../../i18n";

export type ConsoleTab = "gpu" | "terminal" | "files" | "processes" | "services";

/** Remote operations on one machine (docs/agent-upgrade.md §1, §2), in a right-hand sheet. */
export function HostConsole({ ws, h, initial = "gpu", onClose }: { ws: string; h: Host; initial?: ConsoleTab; onClose: () => void }) {
  const [tab, setTab] = useState<ConsoleTab>(initial);
  return (
    <Dialog xwide title={h.name} onClose={onClose}>
      <div className="space-y-4">
        <Tabs<ConsoleTab> value={tab} onChange={setTab} tabs={[
          { key: "gpu", label: "GPU", count: h.hostInfo?.gpus.length || undefined },
          { key: "terminal", label: t("Terminal") },
          { key: "files", label: t("Files") },
          { key: "processes", label: t("Processes") },
          { key: "services", label: t("Services") },
        ]} />
        {tab === "gpu" ? <GpuPanel ws={ws} hostId={h.id} />
          : tab === "terminal" ? <TerminalPanel ws={ws} hostId={h.id} />
          : tab === "files" ? <FilesPanel ws={ws} hostId={h.id} />
          : tab === "processes" ? <ProcessesPanel ws={ws} hostId={h.id} />
          : <ServicesPanel ws={ws} hostId={h.id} />}
      </div>
    </Dialog>
  );
}
