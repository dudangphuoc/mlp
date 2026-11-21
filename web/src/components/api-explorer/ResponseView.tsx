import { useState } from "react";
import type { JsonValue } from "@bufbuild/protobuf";
import { Tabs } from "../ui";
import { translate as t } from "../../i18n";
import type { CallResult } from "../../lib/rpcInvoke";

function firstTable(json: JsonValue | undefined): Record<string, JsonValue>[] | null {
  if (Array.isArray(json)) return json.filter((x) => x && typeof x === "object") as Record<string, JsonValue>[];
  if (!json || typeof json !== "object") return null;
  const arr = Object.values(json).find((v) => Array.isArray(v) && v.length > 0 && typeof v[0] === "object");
  return (arr as Record<string, JsonValue>[] | undefined) ?? null;
}

function cell(v: JsonValue): string {
  if (v === null || v === undefined) return "";
  if (typeof v === "object") return JSON.stringify(v).slice(0, 80);
  return String(v);
}

export function ResponseView({ result, onUseId }: { result: CallResult; onUseId?: (id: string) => void }) {
  const [tab, setTab] = useState<"data" | "raw">("data");
  const json = result.events ? (result.events as JsonValue) : result.json;
  const rows = firstTable(json);
  const columns = rows ? [...new Set(rows.slice(0, 20).flatMap((r) => Object.keys(r)))].slice(0, 6) : [];
  const size = json === undefined ? 0 : JSON.stringify(json).length;
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <span className={`badge ${result.ok ? "bg-emerald-50 text-emerald-700" : "bg-rose-50 text-rose-700"}`}>{result.status}</span>
        <span className="text-fg-muted">{Math.round(result.ms)} ms</span>
        <span className="text-fg-muted">{size} B</span>
      </div>
      {result.error && <div className="rounded-md border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">{result.error}</div>}
      {json !== undefined && (
        <>
          <Tabs tabs={[{ key: "data", label: t("Data") }, { key: "raw", label: "JSON" }]} value={tab} onChange={setTab} />
          {tab === "data" && rows && rows.length > 0 ? (
            <div className="overflow-x-auto rounded-md border border-line">
              <table className="w-full text-xs">
                <thead><tr className="bg-surface-2">{columns.map((c) => <th key={c} className="px-2 py-1 text-left font-medium">{c}</th>)}</tr></thead>
                <tbody>
                  {rows.slice(0, 100).map((r, i) => (
                    <tr key={i} className="border-t border-line">
                      {columns.map((c) => (
                        <td key={c} className="px-2 py-1 font-mono">
                          {c === "id" && onUseId ? <button className="underline" onClick={() => onUseId(String(r[c]))}>{cell(r[c])}</button> : cell(r[c])}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <pre className="max-h-96 overflow-auto rounded-md bg-surface-2 p-3 text-xs">{JSON.stringify(json, null, 2)}</pre>
          )}
        </>
      )}
    </div>
  );
}
