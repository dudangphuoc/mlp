import { Check, Lock, X } from "lucide-react";
import { ROLES, allows, type Operation } from "../../lib/openapi";
import { translate as t } from "../../i18n";

export function RoleStrip({ op }: { op: Operation }) {
  return (
    <div className="flex flex-wrap gap-1.5">
      {ROLES.map((role) => {
        const fit = allows(op, role);
        return (
          <span key={role} className={`badge ${fit === true ? "bg-emerald-50 text-emerald-700" : fit === "entity" ? "bg-amber-50 text-amber-700" : "bg-surface-2 text-fg-subtle"}`}>
            {fit === true ? <Check className="h-3 w-3" /> : fit === "entity" ? <Lock className="h-3 w-3" /> : <X className="h-3 w-3" />}{t(role)}
          </span>
        );
      })}
    </div>
  );
}
