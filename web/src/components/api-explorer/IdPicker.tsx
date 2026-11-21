import { useQuery } from "@tanstack/react-query";
import type { JsonValue } from "@bufbuild/protobuf";
import { findMethod, invokeUnary } from "../../lib/rpcInvoke";
import { DeploymentSlugs } from "./DeploymentSlugs";

type Row = { id: string; label: string };

function rowsOf(json: JsonValue | undefined): Row[] {
  if (!json || typeof json !== "object" || Array.isArray(json)) return [];
  const list = Object.values(json).find((v) => Array.isArray(v) && v.some((x) => x && typeof x === "object" && "id" in x)) as Record<string, JsonValue>[] | undefined;
  DeploymentSlugs.remember(list ?? []);
  return (list ?? []).map((x) => ({
    id: String(x.id),
    label: String(x.name ?? x.displayName ?? x.slug ?? x.title ?? x.path ?? x.id),
  }));
}

export function IdPicker({ list, workspaceId, value, onChange }:
  { list: string; workspaceId: string; value: string; onChange: (v: string) => void }) {
  const [service, method] = list.split(".");
  const desc = findMethod(`mlplatform.v1.${service}`, method);
  const rows = useQuery({
    queryKey: ["api-explorer-ids", list, workspaceId],
    enabled: !!desc,
    queryFn: async () => rowsOf((await invokeUnary(desc!, { workspaceId })).json),
  });
  if (!desc || !rows.data?.length) return <input className="input font-mono text-xs" value={value} onChange={(e) => onChange(e.target.value)} />;
  return (
    <select className="input" value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">—</option>
      {rows.data.map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
    </select>
  );
}
