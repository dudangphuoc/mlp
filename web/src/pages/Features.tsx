import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ago } from "../lib/format";
import { Dialog, EmptyState, ErrorBox, Field, PageHeader, StateBadge } from "../components/ui";
import type { FeatureTableResponse } from "../gen/mlplatform/v1/feature_pb";
import { translate as t } from "../i18n";

const stateTone: Record<string, string> = { ready: "succeeded", pending: "queued", failed: "failed" };

function Table({ t }: { t: FeatureTableResponse }) {
  const cell = (v: { kind: { case?: string; value?: unknown } }) => (v.kind.case === "nullValue" || v.kind.case === undefined ? "∅" : String(v.kind.value));
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead><tr className="text-left text-xs text-slate-500">{t.columns.map((c) => <th key={c} className="py-1 pr-3">{c}</th>)}</tr></thead>
        <tbody>{t.rows.map((r, i) => (
          <tr key={i} className="border-t border-slate-100 font-mono text-xs">{r.values.map((v, j) => <td key={j} className={`py-1 pr-3 ${v.kind.case === "nullValue" ? "text-slate-300" : ""}`}>{cell(v)}</td>)}</tr>))}</tbody>
      </table>
    </div>
  );
}

/** M07 — Feature store: entities, feature views (source, schema, TTL, materialize schedule, freshness), online value browser, point-in-time lookup. */
export function FeaturesPage({ ws }: { ws: string }) {
  const qc = useQueryClient();
  const entities = useQuery({ queryKey: ["entities", ws], queryFn: () => api.feature.listEntities({ workspaceId: ws }) });
  const views = useQuery({ queryKey: ["fviews", ws], queryFn: () => api.feature.listViews({ workspaceId: ws }), refetchInterval: 10000 });
  const datasets = useQuery({ queryKey: ["datasets", ws, ""], queryFn: () => api.data.listDatasets({ workspaceId: ws, task: "" }) });
  const [sel, setSel] = useState("");
  const [newEntity, setNewEntity] = useState<{ name: string; joinKey: string } | null>(null);
  const [newView, setNewView] = useState<{ name: string; entity: string; datasetId: string; versionId: string; ts: string; ttl: number; online: boolean; every: number } | null>(null);
  const versions = useQuery({ queryKey: ["versions", ws, newView?.datasetId], enabled: !!newView?.datasetId, queryFn: () => api.data.listVersions({ workspaceId: ws, datasetId: newView!.datasetId }) });
  const refresh = () => ["entities", "fviews"].forEach((k) => qc.invalidateQueries({ queryKey: [k, ws] }));
  const createEntity = useMutation({ mutationFn: () => api.feature.createEntity({ workspaceId: ws, name: newEntity!.name, joinKey: newEntity!.joinKey, description: "" }),
    onSuccess: () => { refresh(); setNewEntity(null); } });
  const createView = useMutation({
    mutationFn: () => api.feature.createView({ workspaceId: ws, name: newView!.name, entity: newView!.entity, sourceVersionId: newView!.versionId, timestampColumn: newView!.ts,
      features: [], ttlHours: newView!.ttl, online: newView!.online, materializeEveryMin: newView!.every }),
    onSuccess: (v) => { refresh(); setNewView(null); setSel(v.id); },
  });
  const materialize = useMutation({ mutationFn: (id: string) => api.feature.materialize({ workspaceId: ws, viewId: id }), onSuccess: refresh });
  const list = views.data?.views ?? [];
  const v = list.find((x) => x.id === sel);
  const sample = useQuery({ queryKey: ["fonline", ws, sel, String(v?.lastMaterializedAt?.seconds ?? "")], enabled: !!v?.online && v.state === "ready",
    queryFn: () => api.feature.sampleOnline({ workspaceId: ws, viewId: sel, limit: 50 }) });
  const [pit, setPit] = useState({ key: "", at: "" });
  const lookup = useMutation({
    mutationFn: () => api.feature.getHistoricalFeatures({ workspaceId: ws, features: v!.features.map((f) => `${v!.name}@${v!.version}:${f.name}`),
      spine: [{ entityKey: pit.key, at: { seconds: BigInt(Math.floor(new Date(pit.at).getTime() / 1000)), nanos: 0 } }] }),
  });
  return (
    <>
      <PageHeader title="Feature store"
        actions={<>
          <button className="btn-ghost" onClick={() => setNewEntity({ name: "", joinKey: "" })}>+ Entity</button>
          <button className="btn-primary" disabled={!entities.data?.entities.length} onClick={() => setNewView({ name: "", entity: entities.data!.entities[0].name, datasetId: "", versionId: "", ts: "event_time", ttl: 720, online: true, every: 0 })}>+ Feature view</button>
        </>} />
      <ErrorBox error={views.error ? errorText(views.error) : materialize.error ? errorText(materialize.error) : null} />
      <div className="mb-3 flex flex-wrap gap-2 text-xs text-slate-500">
        Entity: {(entities.data?.entities ?? []).map((e) => <span key={e.id} className="rounded bg-slate-100 px-2 py-0.5">{e.name} ({e.joinKey})</span>)}
      </div>
      {list.length === 0 ? <EmptyState title={t("No feature views yet.")} /> : (
        <div className="grid gap-4 md:grid-cols-3">
          <div className="card p-0">
            {list.map((x) => (
              <button key={x.id} className={`block w-full border-b border-slate-100 px-3 py-2 text-left text-sm ${sel === x.id ? "bg-brand-50" : "hover:bg-slate-50"}`} onClick={() => setSel(x.id)}>
                <div className="flex items-center justify-between"><span className="font-medium">{x.name} <span className="text-xs text-slate-400">v{x.version}</span></span><StateBadge state={stateTone[x.state] ?? x.state} /></div>
                <div className="text-xs text-slate-500">{x.entity} · {t("{n} features", { n: x.features.length })} · {t("{n} rows", { n: String(x.rows) })}</div>
              </button>))}
          </div>
          <div className="space-y-4 md:col-span-2">
            {!v ? <div className="card text-sm text-slate-500">{t("Select a feature view.")}</div> : (
              <>
                <div className="card space-y-2 text-sm">
                  <div className="flex items-center justify-between">
                    <span className="font-medium">{v.name} v{v.version}</span>
                    <button className="btn-ghost py-1 text-xs" disabled={materialize.isPending} onClick={() => materialize.mutate(v.id)}>{t("Re-materialize")}</button>
                  </div>
                  {v.state === "failed" && <div className="rounded bg-rose-50 p-2 text-xs text-rose-700">{t(v.stateReason)}</div>}
                  <div className="grid grid-cols-2 gap-x-4 text-xs text-slate-600">
                    <div>Entity: {v.entity} ({t("key")} <code>{v.joinKey}</code>)</div><div>{t("Event time:")} <code>{v.timestampColumn}</code></div>
                    <div>TTL: {t("{n} h", { n: v.ttlHours })}</div><div>{t("Schedule")}: {v.materializeEveryMin ? t("every {n} min", { n: v.materializeEveryMin }) : t("manual")} · online {v.online ? t("on") : t("off")}</div>
                    <div>Materialize: {v.lastMaterializedAt ? ago(v.lastMaterializedAt) : "—"}</div><div>{t("Freshness (latest event):")} {v.maxEventTs ? ago(v.maxEventTs) : "—"}</div>
                    <div>{t("{n} rows", { n: String(v.rows) })} · {t("{n} entities", { n: String(v.entities) })}</div>
                  </div>
                  <div className="flex flex-wrap gap-1">{v.features.map((f) => <span key={f.name} className="rounded bg-slate-100 px-2 py-0.5 font-mono text-xs">{f.name}: {f.type}</span>)}</div>
                  <div className="text-xs text-slate-500">{t("Use in training/serving:")} <code>{v.name}@{v.version}:&lt;feature&gt;</code></div>
                </div>
                <div className="card">
                  <div className="mb-2 text-sm font-medium">{t("Point-in-time lookup (values as of a moment, no future leakage)")}</div>
                  <div className="flex items-end gap-2">
                    <Field label={v.joinKey}><input className="input" value={pit.key} onChange={(e) => setPit({ ...pit, key: e.target.value })} /></Field>
                    <Field label={t("Time")}><input className="input" type="datetime-local" value={pit.at} onChange={(e) => setPit({ ...pit, at: e.target.value })} /></Field>
                    <button className="btn-primary" disabled={!pit.key || !pit.at || lookup.isPending} onClick={() => lookup.mutate()}>Tra</button>
                  </div>
                  <ErrorBox error={lookup.error ? errorText(lookup.error) : null} />
                  {lookup.data && <div className="mt-2"><Table t={lookup.data} /></div>}
                </div>
                {v.online && sample.data && (
                  <div className="card"><div className="mb-2 text-sm font-medium">{t("Online values (latest per entity)")}</div><Table t={sample.data} /></div>)}
              </>)}
          </div>
        </div>)}
      {newEntity && (
        <Dialog title={t("New entity")} onClose={() => setNewEntity(null)}>
          <div className="space-y-3">
            <Field label={t("Name")}><input className="input" value={newEntity.name} onChange={(e) => setNewEntity({ ...newEntity, name: e.target.value })} placeholder="customer" /></Field>
            <Field label={t("Key column")}><input className="input" value={newEntity.joinKey} onChange={(e) => setNewEntity({ ...newEntity, joinKey: e.target.value })} placeholder="customer_id" /></Field>
            <ErrorBox error={createEntity.error ? errorText(createEntity.error) : null} />
            <button className="btn-primary" disabled={!newEntity.name || !newEntity.joinKey || createEntity.isPending} onClick={() => createEntity.mutate()}>{t("Created")}</button>
          </div>
        </Dialog>)}
      {newView && (
        <Dialog title={t("New feature view")} onClose={() => setNewView(null)} wide>
          <div className="space-y-3">
            <div className="grid gap-3 md:grid-cols-2">
              <Field label={t("Name")}><input className="input" value={newView.name} onChange={(e) => setNewView({ ...newView, name: e.target.value })} /></Field>
              <Field label="Entity"><select className="input" value={newView.entity} onChange={(e) => setNewView({ ...newView, entity: e.target.value })}>
                {(entities.data?.entities ?? []).map((e) => <option key={e.id} value={e.name}>{e.name}</option>)}</select></Field>
              <Field label={t("Source dataset")}><select className="input" value={newView.datasetId} onChange={(e) => setNewView({ ...newView, datasetId: e.target.value, versionId: "" })}>
                <option value="">—</option>{(datasets.data?.datasets ?? []).map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}</select></Field>
              <Field label="Version"><select className="input" value={newView.versionId} onChange={(e) => setNewView({ ...newView, versionId: e.target.value })}>
                <option value="">—</option>{(versions.data?.versions ?? []).map((x) => <option key={x.id} value={x.id}>v{x.number}</option>)}</select></Field>
              <Field label={t("Event time column")}><input className="input" value={newView.ts} onChange={(e) => setNewView({ ...newView, ts: e.target.value })} /></Field>
              <Field label={t("TTL (hours)")}><input className="input" type="number" value={newView.ttl} onChange={(e) => setNewView({ ...newView, ttl: Number(e.target.value) })} /></Field>
              <Field label={t("Materialize every (minutes, 0 = manual)")}><input className="input" type="number" value={newView.every} onChange={(e) => setNewView({ ...newView, every: Number(e.target.value) })} /></Field>
              <label className="flex items-center gap-2 pt-6 text-sm"><input type="checkbox" checked={newView.online} onChange={(e) => setNewView({ ...newView, online: e.target.checked })} />{t("Enable online (for inference)")}</label>
            </div>
            <ErrorBox error={createView.error ? errorText(createView.error) : null} />
            <button className="btn-primary" disabled={!newView.name || !newView.versionId || createView.isPending} onClick={() => createView.mutate()}>{t("Create & materialize")}</button>
          </div>
        </Dialog>)}
    </>
  );
}
