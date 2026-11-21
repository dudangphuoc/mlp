import { useMemo, useState, type ReactNode } from "react";
import { pageKey, readSticky, useSticky, writeSticky } from "../lib/persist";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { translate as t } from "../i18n";

/*
 * DataTable + CollectionView — 30/01 §4. Column sorting, search, show/hide columns, pagination, CSV export, multi-select + bulk actions;
 * CollectionView adds filters and a Grid / Compact / Table switch. State (search, sort, view mode) lives in the URL so links can be shared (30/01 §5).
 */

export type Column<T> = {
  key: string;
  header: string;
  cell: (row: T) => ReactNode;
  /** Value used for sort / search / CSV export (not sortable by default). */
  value?: (row: T) => string | number | bigint | undefined;
  align?: "right";
  width?: string;
  hidden?: boolean;
};

/** State mirrored in the URL (shareable links) and remembered per page in localStorage (survives reloads and navigation). */
function useUrlState(key: string, initial: string): [string, (v: string) => void] {
  const store = `table:${pageKey()}:${key}`;
  const [v, setV] = useState(() => new URLSearchParams(window.location.search).get(key) ?? readSticky(store, initial));
  const set = (nv: string) => {
    setV(nv);
    writeSticky(store, nv === initial ? "" : nv);
    const u = new URL(window.location.href);
    if (nv === initial) u.searchParams.delete(key); else u.searchParams.set(key, nv);
    window.history.replaceState(window.history.state, "", u);
  };
  return [v, set];
}

const cmp = (a: unknown, b: unknown) =>
  typeof a === "number" && typeof b === "number" ? a - b
  : typeof a === "bigint" && typeof b === "bigint" ? (a < b ? -1 : a > b ? 1 : 0)
  : String(a ?? "").localeCompare(String(b ?? ""), "vi", { numeric: true });

function csv<T>(rows: T[], cols: Column<T>[]) {
  cols = cols.filter((c) => c.header.trim() && c.value);   // skip action columns / columns without an export value
  const esc = (s: string) => (/[",\n]/.test(s) ? `"${s.replaceAll('"', '""')}"` : s);
  const lines = [cols.map((c) => esc(c.header)).join(","), ...rows.map((r) => cols.map((c) => esc(String(c.value?.(r) ?? ""))).join(","))];
  const url = URL.createObjectURL(new Blob(["﻿" + lines.join("\n")], { type: "text/csv;charset=utf-8" }));
  const a = Object.assign(document.createElement("a"), { href: url, download: "export.csv" });
  a.click();
  URL.revokeObjectURL(url);
}

export function DataTable<T>({ rows, columns, rowKey, onRowClick, search = true, pageSize = 50, empty, loading, actions, selectable, bulk, urlKey = "" }: {
  rows: T[]; columns: Column<T>[]; rowKey: (r: T) => string; onRowClick?: (r: T) => void; search?: boolean; pageSize?: number;
  empty?: ReactNode; loading?: boolean; actions?: ReactNode; selectable?: boolean; bulk?: (selected: T[], clear: () => void) => ReactNode; urlKey?: string;
}) {
  const [q, setQ] = useUrlState(urlKey + "q", "");
  const [sort, setSort] = useUrlState(urlKey + "sort", "");
  const tableKey = `table:${pageKey()}:${urlKey}`;
  const [hiddenList, setHiddenList] = useSticky<string[]>(tableKey + "hidden", columns.filter((c) => c.hidden).map((c) => c.key));
  const hiddenCols = useMemo(() => new Set(hiddenList), [hiddenList]);
  const setHidden = (f: (h: Set<string>) => Set<string>) => setHiddenList((old) => [...f(new Set(old))]);
  const [page, setPage] = useSticky<number>(tableKey + "page", 0);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const cols = columns.filter((c) => !hiddenCols.has(c.key));
  const [sortKey, dir] = sort ? sort.split(":") : ["", "asc"];

  const view = useMemo(() => {
    const needle = q.trim().toLowerCase();
    let r = needle ? rows.filter((row) => columns.some((c) => String(c.value?.(row) ?? "").toLowerCase().includes(needle))) : rows;
    const sc = columns.find((c) => c.key === sortKey);
    if (sc?.value) r = [...r].sort((a, b) => cmp(sc.value!(a), sc.value!(b)) * (dir === "desc" ? -1 : 1));
    return r;
  }, [rows, columns, q, sortKey, dir]);
  const pages = Math.max(1, Math.ceil(view.length / pageSize));
  const pg = Math.min(page, pages - 1);   // remembered page may be past the end after data changed
  const shown = view.slice(pg * pageSize, pg * pageSize + pageSize);
  const selected = rows.filter((r) => sel.has(rowKey(r)));

  return (
    <div className="card overflow-hidden p-0">
      <div className="flex flex-wrap items-center gap-2 border-b border-line px-3 py-2">
        {search && <input className="input h-8 w-full sm:max-w-xs" placeholder={t("Search…")} value={q} onChange={(e) => { setQ(e.target.value); setPage(0); }} />}
        <span className="text-xs text-fg-subtle">{t("{n} rows", { n: view.length })}</span>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          {actions}
          <Menu.Root>
            <Menu.Trigger className="btn-ghost h-8 px-2 text-xs">{t("Columns ▾")}</Menu.Trigger>
            <Menu.Portal>
              <Menu.Content align="end" sideOffset={4} className="z-50 min-w-44 rounded-lg border border-line bg-overlay p-1 text-sm shadow-xl">
                {columns.filter((c) => c.header.trim()).map((c) => (   // action columns (no header) are always shown and cannot be hidden
                  <Menu.CheckboxItem key={c.key} checked={!hiddenCols.has(c.key)} onSelect={(e) => e.preventDefault()}
                    onCheckedChange={(on) => setHidden((h) => { const n = new Set(h); if (on) n.delete(c.key); else n.add(c.key); return n; })}
                    className="flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 outline-none data-[highlighted]:bg-surface-2">
                    <span className="w-3 text-primary">{hiddenCols.has(c.key) ? "" : "✓"}</span>{c.header}
                  </Menu.CheckboxItem>))}
              </Menu.Content>
            </Menu.Portal>
          </Menu.Root>
          <button className="btn-ghost h-8 px-2 text-xs" onClick={() => csv(view, cols)} disabled={view.length === 0}>CSV</button>
        </div>
      </div>
      {selectable && selected.length > 0 && bulk && (
        <div className="flex items-center gap-3 border-b border-line bg-primary/10 px-3 py-2 text-sm">
          <span>{t("Selected {n}", { n: selected.length })}</span>{bulk(selected, () => setSel(new Set()))}
        </div>)}
      <div className="overflow-x-auto">
        <table className="w-full min-w-max text-sm">
          <thead>
            <tr className="border-b border-line text-left text-xs text-fg-subtle">
              {selectable && <th className="w-8 px-3 py-2"><input type="checkbox" checked={shown.length > 0 && shown.every((r) => sel.has(rowKey(r)))}
                onChange={(e) => setSel((s) => { const n = new Set(s); shown.forEach((r) => (e.target.checked ? n.add(rowKey(r)) : n.delete(rowKey(r)))); return n; })} /></th>}
              {cols.map((c) => (
                <th key={c.key} style={{ width: c.width }} className={`whitespace-nowrap px-3 py-2 font-medium ${c.align === "right" ? "text-right" : ""}`}>
                  {c.value ? (
                    <button className="inline-flex items-center gap-1 hover:text-fg" onClick={() => setSort(sortKey === c.key ? (dir === "asc" ? `${c.key}:desc` : "") : `${c.key}:asc`)}>
                      {c.header}<span className="text-[10px]">{sortKey === c.key ? (dir === "asc" ? "▲" : "▼") : ""}</span>
                    </button>) : c.header}
                </th>))}
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {loading ? Array.from({ length: 4 }, (_, i) => (
              <tr key={i}><td colSpan={cols.length + (selectable ? 1 : 0)} className="px-3 py-2"><div className="skeleton h-6" /></td></tr>))
              : shown.length === 0 ? (
                <tr><td colSpan={cols.length + (selectable ? 1 : 0)} className="px-3 py-10 text-center text-fg-subtle">{q ? t("No matching rows.") : empty ?? t("No data yet.")}</td></tr>)
              : shown.map((r) => (
                <tr key={rowKey(r)} className={`${onRowClick ? "cursor-pointer" : ""} hover:bg-surface-2`} onClick={() => onRowClick?.(r)}>
                  {selectable && <td className="px-3 py-2" onClick={(e) => e.stopPropagation()}>
                    <input type="checkbox" checked={sel.has(rowKey(r))} onChange={(e) => setSel((s) => { const n = new Set(s); if (e.target.checked) n.add(rowKey(r)); else n.delete(rowKey(r)); return n; })} /></td>}
                  {cols.map((c) => <td key={c.key} className={`max-w-[28rem] overflow-hidden text-ellipsis whitespace-nowrap px-3 py-2 ${c.align === "right" ? "text-right tabular-nums" : ""}`}>{c.cell(r)}</td>)}
                </tr>))}
          </tbody>
        </table>
      </div>
      {pages > 1 && (
        <div className="flex items-center justify-end gap-2 border-t border-line px-3 py-2 text-xs text-fg-muted">
          <button className="btn-ghost h-7 px-2" disabled={pg === 0} onClick={() => setPage(pg - 1)}>‹</button>
          {t("Page {n}/{total}", { n: pg + 1, total: pages })}
          <button className="btn-ghost h-7 px-2" disabled={pg >= pages - 1} onClick={() => setPage(pg + 1)}>›</button>
        </div>)}
    </div>
  );
}

/** Resource list: search + filter + Grid / Table switch (CollectionView 30/01 §4). */
export function CollectionView<T>({ rows, columns, rowKey, card, filters, onOpen, loading, empty, urlKey = "" }: {
  rows: T[]; columns: Column<T>[]; rowKey: (r: T) => string; card: (r: T) => ReactNode;
  filters?: { key: string; label: string; options: { value: string; label: string }[]; test: (r: T, v: string) => boolean }[];
  onOpen?: (r: T) => void; loading?: boolean; empty?: ReactNode; urlKey?: string;
}) {
  const [mode, setMode] = useUrlState(urlKey + "view", "grid");
  const filterKey = `table:${pageKey()}:${urlKey}filters`;
  const [fv, setFv] = useState<Record<string, string>>(() => {
    const saved = readSticky<Record<string, string>>(filterKey, {});
    return Object.fromEntries((filters ?? []).map((f) => [f.key, new URLSearchParams(window.location.search).get(urlKey + f.key) ?? saved[f.key] ?? ""]));
  });
  const [q, setQ] = useUrlState(urlKey + "q", "");
  const filtered = rows.filter((r) => (filters ?? []).every((f) => !fv[f.key] || f.test(r, fv[f.key])));
  const searched = q ? filtered.filter((r) => columns.some((c) => String(c.value?.(r) ?? "").toLowerCase().includes(q.toLowerCase()))) : filtered;
  const setFilter = (k: string, v: string) => {
    setFv((s) => { const n = { ...s, [k]: v }; writeSticky(filterKey, n); return n; });
    const u = new URL(window.location.href);
    if (v) u.searchParams.set(urlKey + k, v); else u.searchParams.delete(urlKey + k);
    window.history.replaceState(window.history.state, "", u);
  };
  const filterBar = (filters ?? []).map((f) => (
    <select key={f.key} className="input h-8 w-auto" value={fv[f.key]} onChange={(e) => setFilter(f.key, e.target.value)}>
      <option value="">{t("{f}: all", { f: t(f.label) })}</option>{f.options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
    </select>));
  const toggle = (
    <div className="flex rounded-md border border-line p-0.5">
      {([["grid", "▦ Grid"], ["table", "☰ Table"]] as const).map(([k, l]) => (
        <button key={k} onClick={() => setMode(k)} className={`rounded px-2 py-1 text-xs ${mode === k ? "bg-surface-2 text-fg" : "text-fg-muted"}`}>{t(l)}</button>))}
    </div>);
  if (mode === "table")
    return <DataTable rows={filtered} columns={columns} rowKey={rowKey} onRowClick={onOpen} loading={loading} empty={empty} urlKey={urlKey} actions={<>{filterBar}{toggle}</>} />;
  return (
    <div>
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <input className="input h-8 max-w-xs" placeholder={t("Search…")} value={q} onChange={(e) => setQ(e.target.value)} />
        {filterBar}<span className="text-xs text-fg-subtle">{t("{n} items", { n: searched.length })}</span>
        <div className="ml-auto">{toggle}</div>
      </div>
      {loading ? <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 min-[2200px]:grid-cols-5">{[0, 1, 2].map((i) => <div key={i} className="skeleton h-32" />)}</div>
        : searched.length === 0 ? <div className="card py-12 text-center text-sm text-fg-subtle">{q || Object.values(fv).some(Boolean) ? t("No matching items.") : empty ?? t("No data yet.")}</div>
        : <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 min-[2200px]:grid-cols-5">{searched.map((r) => <div key={rowKey(r)}>{card(r)}</div>)}</div>}
    </div>
  );
}
