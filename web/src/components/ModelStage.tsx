import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { Dialog, ErrorBox, Field } from "./ui";
import { translate as t } from "../i18n";

export const STAGES = ["none", "staging", "production", "archived"] as const;

/** Stage of a model version as a select: any stage can be set at any time (also back from production); production asks for a reason. */
export function StageControl({ ws, version }: { ws: string; version: { id: string; version: number; stage: string } }) {
  const qc = useQueryClient();
  const [reasonFor, setReasonFor] = useState(false);
  const [reason, setReason] = useState("");
  const move = useMutation({
    mutationFn: (p: { stage: string; reason: string }) => api.registry.transitionStage({ workspaceId: ws, versionId: version.id, stage: p.stage, reason: p.reason }),
    onSuccess: () => {
      setReasonFor(false); setReason("");
      void qc.invalidateQueries({ queryKey: ["mversions", ws] }); void qc.invalidateQueries({ queryKey: ["models", ws] }); void qc.invalidateQueries({ queryKey: ["project-versions", ws] });
    },
  });
  return (
    <span className="inline-flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
      <select className={`input h-7 w-32 py-0 text-xs ${version.stage === "production" ? "border-emerald-500/60 text-emerald-500" : ""}`} aria-label="Stage" disabled={move.isPending}
        value={version.stage || "none"} onChange={(e) => (e.target.value === "production" ? setReasonFor(true) : move.mutate({ stage: e.target.value, reason: "" }))}>
        {STAGES.map((s) => <option key={s} value={s}>{t(s)}</option>)}
      </select>
      {move.error && !reasonFor && <span className="text-xs text-danger">{errorText(move.error)}</span>}
      {reasonFor && (
        <Dialog title={t("Promote v{v} to production?", { v: version.version })} onClose={() => setReasonFor(false)}>
          <div className="space-y-3">
            <Field label={t("Reason")}><textarea className="input min-h-20" value={reason} onChange={(e) => setReason(e.target.value)} autoFocus /></Field>
            <ErrorBox error={move.error ? errorText(move.error) : null} />
            <div className="flex justify-end gap-2">
              <button className="btn-ghost" onClick={() => setReasonFor(false)}>{t("Cancel")}</button>
              <button className="btn-primary" disabled={!reason.trim() || move.isPending} onClick={() => move.mutate({ stage: "production", reason })}>{t("Promote to production")}</button>
            </div>
          </div>
        </Dialog>)}
    </span>
  );
}
