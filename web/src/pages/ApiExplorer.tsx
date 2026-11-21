import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Braces, Lock, Radio, Search } from "lucide-react";
import { EmptyState, ErrorBox, PageHeader, Skeleton } from "../components/ui";
import { clock, day } from "../lib/format";
import { OperationSheet } from "../components/api-explorer/OperationSheet";
import { readHistory } from "../components/api-explorer/history";
import { translate as t } from "../i18n";
import { ROLES, allows, loadApiDocument, type Effect, type Operation } from "../lib/openapi";

const EFFECTS: Effect[] = ["read", "write", "delete", "compute"];

export function ApiExplorerPage({ ws, myRole }: { ws: string; myRole: string }) {
  const doc = useQuery({ queryKey: ["openapi"], queryFn: loadApiDocument, staleTime: Infinity });
  const [search, setSearch] = useState("");
  const [effects, setEffects] = useState<Effect[]>([]);
  const [role, setRole] = useState(myRole);
  const [showInternal, setShowInternal] = useState(false);
  const [writes, setWrites] = useState(false);
  const [open, setOpen] = useState<Operation | null>(() => {
    const id = new URLSearchParams(window.location.search).get("op");
    return id ? ({ id } as Operation) : null;
  });
  const history = useMemo(() => readHistory(), [open]);

  const ops = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (doc.data?.operations ?? []).filter((op) =>
      (showInternal || op.audience === "user") &&
      (effects.length === 0 || effects.includes(op.effect)) &&
      (!q || `${op.id} ${op.path} ${op.permission ?? ""} ${op.module}`.toLowerCase().includes(q)));
  }, [doc.data, search, effects, showInternal]);
  const groups = useMemo(() => {
    const map = new Map<string, Operation[]>();
    for (const op of ops) map.set(op.service, [...(map.get(op.service) ?? []), op]);
    return [...map.entries()];
  }, [ops]);
  const selected = open && doc.data ? doc.data.operations.find((o) => o.id === open.id) ?? null : null;

  const select = (op: Operation | null) => {
    setOpen(op);
    const url = new URL(window.location.href);
    if (op) url.searchParams.set("op", op.id); else url.searchParams.delete("op");
    window.history.replaceState(null, "", url);
  };

  return (
    <div>
      <PageHeader title={t("API")} icon={<Braces className="h-5 w-5" />}
        badges={doc.data && <span className="badge bg-surface-2 text-fg-muted">{doc.data.operations.length}</span>} />
      <ErrorBox error={doc.error ? (doc.error as Error).message : null} onRetry={() => void doc.refetch()} />
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative min-w-60 flex-1">
          <Search className="pointer-events-none absolute left-2.5 top-2.5 h-4 w-4 text-fg-subtle" />
          <input className="input pl-8" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("Search")} />
        </div>
        {EFFECTS.map((e) => (
          <button key={e} className={`rounded-full border px-2.5 py-1 text-xs ${effects.includes(e) ? "border-fg bg-surface-2 text-fg" : "border-line text-fg-muted"}`}
            onClick={() => setEffects(effects.includes(e) ? effects.filter((x) => x !== e) : [...effects, e])}>{t(e)}</button>
        ))}
        <select className="input w-auto" value={role} onChange={(e) => setRole(e.target.value)} aria-label={t("Role")}>
          {ROLES.map((r) => <option key={r} value={r}>{t(r)}</option>)}
        </select>
        <label className="flex items-center gap-1.5 text-sm"><input type="checkbox" checked={showInternal} onChange={(e) => setShowInternal(e.target.checked)} />{t("Internal")}</label>
        <label className="flex items-center gap-1.5 text-sm"><input type="checkbox" checked={writes} onChange={(e) => setWrites(e.target.checked)} />{t("Writes")}</label>
      </div>
      {doc.isLoading && <Skeleton rows={8} />}
      {doc.data && ops.length === 0 && (
        <EmptyState title={t("No results")} action={<button className="btn-secondary" onClick={() => { setSearch(""); setEffects([]); }}>{t("Clear filters")}</button>} />
      )}
      <div className="space-y-4">
        {groups.map(([service, list]) => (
          <div key={service} className="card overflow-hidden p-0">
            <div className="flex items-center justify-between border-b border-line bg-surface-2 px-4 py-2 text-sm font-medium">
              <span>{service}</span><span className="text-fg-muted">{list.length}</span>
            </div>
            <div className="divide-y divide-line">
              {list.map((op) => {
                const fit = allows(op, role);
                return (
                  <button key={op.id} onClick={() => select(op)}
                    className={`flex w-full items-center gap-3 px-4 py-2 text-left text-sm hover:bg-surface-2 ${fit === false ? "opacity-50" : ""}`}>
                    <span className={`w-16 shrink-0 text-xs font-medium ${op.effect === "read" ? "text-fg-muted" : op.effect === "delete" ? "text-rose-600" : "text-amber-600"}`}>{t(op.effect)}</span>
                    <span className="min-w-0 flex-1 truncate font-mono text-xs">{op.grpc ? op.grpc.method : `${op.verb} ${op.path}`}</span>
                    {op.streaming && <Radio className="h-3.5 w-3.5 text-fg-subtle" />}
                    {fit === false && <Lock className="h-3.5 w-3.5 text-fg-subtle" />}
                    <span className="hidden shrink-0 font-mono text-xs text-fg-subtle sm:inline">{op.permission ?? op.access}</span>
                  </button>
                );
              })}
            </div>
          </div>
        ))}
      </div>
      {history.length > 0 && (
        <div className="card mt-6 p-0">
          <div className="border-b border-line bg-surface-2 px-4 py-2 text-sm font-medium">{t("History")}</div>
          <div className="divide-y divide-line">
            {history.slice(0, 10).map((h) => (
              <button key={h.at} className="flex w-full items-center gap-3 px-4 py-1.5 text-left text-xs hover:bg-surface-2"
                onClick={() => { const op = doc.data?.operations.find((o) => o.id === h.op); if (op) select(op); }}>
                <span className="min-w-0 flex-1 truncate font-mono">{h.op}</span>
                <span className="w-24 shrink-0">{h.status}</span>
                <span className="w-16 shrink-0 text-right text-fg-muted">{h.ms} ms</span>
                <span className="w-32 shrink-0 text-right text-fg-muted">{day(h.at)} {clock(h.at)}</span>
              </button>
            ))}
          </div>
        </div>
      )}
      {selected && <OperationSheet key={selected.id} op={selected} workspaceId={ws} writesEnabled={writes} onClose={() => select(null)} />}
    </div>
  );
}
