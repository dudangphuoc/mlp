import { AlertTriangle, Globe } from "lucide-react";
import { Dialog } from "./ui";
import { translate as t } from "../i18n";

/** Explicit confirmation before data goes to an external API (asked by the server with LABEL_EXTERNAL_CONFIRM). */
export function ExternalConfirm({ what, message, busy, onConfirm, onClose }:
  { what: string; message: string; busy?: boolean; onConfirm: () => void; onClose: () => void }) {
  return (
    <Dialog title={t("Send to an external API?")} onClose={onClose} description={t("The data leaves the platform. Use this only when the project allows it.")}>
      <div className="space-y-3 text-sm">
        <div className="flex items-start gap-2 rounded-lg border border-warning/30 bg-warning/10 p-3 text-warning"><AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{t(message)}</span></div>
        <p className="text-fg-muted">{t(what)}</p>
        <div className="flex justify-end gap-2 border-t border-line pt-3">
          <button className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
          <button className="btn-primary" disabled={busy} onClick={onConfirm}><Globe className="h-4 w-4" />{t("Send")}</button>
        </div>
      </div>
    </Dialog>
  );
}
