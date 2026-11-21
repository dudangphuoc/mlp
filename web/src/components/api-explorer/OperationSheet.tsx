import { useMemo, useRef, useState } from "react";
import type { JsonValue } from "@bufbuild/protobuf";
import { Play, Square, FlaskConical, ShieldAlert } from "lucide-react";
import { ConfirmDialog, Dialog, Field, HardwareChip, Tabs } from "../ui";
import { ModelTestSheet } from "../ModelTester";
import { translate as t } from "../../i18n";
import type { Operation } from "../../lib/openapi";
import { emptyJson, findMethod, invokeRest, invokeStream, invokeUnary, type CallResult } from "../../lib/rpcInvoke";
import { DeploymentSlugs } from "./DeploymentSlugs";
import { IdPicker } from "./IdPicker";
import { MessageForm } from "./MessageForm";
import { ResponseView } from "./ResponseView";
import { RoleStrip } from "./RoleStrip";
import { Snippets } from "./Snippets";
import { pushHistory } from "./history";

type JsonObject = Record<string, JsonValue>;

function initialBody(op: Operation, workspaceId: string, schemaDefault?: JsonObject): JsonObject {
  if (!op.grpc) return op.path.endsWith("/predict") ? { ws: workspaceId, slug: "", inputs: [{ text: "" }] }
    : op.path.endsWith("/feedback") ? { ws: workspaceId, slug: "", feedback: [] } : {};
  const body = { ...(schemaDefault ?? {}) };
  if (op.scopeField) body.workspaceId = workspaceId;
  return body;
}

export function OperationSheet({ op, workspaceId, writesEnabled, onClose }:
  { op: Operation; workspaceId: string; writesEnabled: boolean; onClose: () => void }) {
  const method = useMemo(() => (op.grpc ? findMethod(op.grpc.service, op.grpc.method) : undefined), [op]);
  const [body, setBody] = useState<JsonObject>(() => initialBody(op, workspaceId, method ? emptyJson(method.input) : undefined));
  const [raw, setRaw] = useState(() => JSON.stringify(body, null, 2));
  const [tab, setTab] = useState<"form" | "json">(method ? "form" : "json");
  const [armed, setArmed] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<CallResult | null>(null);
  const [events, setEvents] = useState<JsonValue[]>([]);
  const [deploymentKey, setDeploymentKey] = useState("");
  const [testing, setTesting] = useState(false);
  const [deploymentId, setDeploymentId] = useState("");
  const abort = useRef<AbortController | null>(null);
  const isWrite = op.effect !== "read";
  const blocked = isWrite && !writesEnabled;

  const update = (next: JsonObject) => { setBody(next); setRaw(JSON.stringify(next, null, 2)); };
  const parsedRaw = (): JsonObject | null => { try { return JSON.parse(raw) as JsonObject; } catch { return null; } };

  async function send() {
    const payload = tab === "json" ? parsedRaw() : body;
    if (!payload) { setResult({ ok: false, status: "JSON", ms: 0, error: t("Invalid JSON") }); return; }
    setBusy(true); setArmed(false); setEvents([]);
    let res: CallResult;
    if (method && op.streaming) {
      abort.current = new AbortController();
      const collected: JsonValue[] = [];
      res = await invokeStream(method, payload, abort.current.signal, (e) => { collected.push(e); setEvents([...collected]); });
      res = { ...res, events: collected };
    } else if (method) res = await invokeUnary(method, payload);
    else res = await invokeRest(op.verb, fillPath(op.path, payload), JSON.stringify(payload),
      { "Content-Type": "application/json", ...(deploymentKey ? { Authorization: `Bearer ${deploymentKey}` } : {}) });
    setResult(res);
    pushHistory({ op: op.id, status: res.status, ms: Math.round(res.ms), at: Date.now(), body: JSON.stringify(payload) });
    setBusy(false);
  }

  function onSend() {
    if (op.effect === "delete") { setConfirmDelete(true); return; }
    if (isWrite && !armed) { setArmed(true); return; }
    void send();
  }

  const targets = Object.entries(tab === "json" ? parsedRaw() ?? {} : body)
    .filter(([k, v]) => k.endsWith("Id") && typeof v === "string" && v.length > 0);

  return (
    <Dialog title={op.grpc ? `${op.service}.${op.grpc.method}` : `${op.verb} ${op.path}`} onClose={onClose} xwide>
      <div className="grid gap-6 lg:grid-cols-2">
        <div className="space-y-4">
          <div className="flex flex-wrap gap-1.5">
            <span className="badge bg-surface-2 text-fg-muted">{op.kind}</span>
            <span className={`badge ${op.effect === "read" ? "bg-surface-2 text-fg-muted" : op.effect === "delete" ? "bg-rose-50 text-rose-700" : "bg-amber-50 text-amber-700"}`}>{t(op.effect)}</span>
            {op.permission && <span className="badge bg-surface-2 font-mono text-fg-muted">{op.permission}</span>}
            {op.module && <span className="badge bg-surface-2 text-fg-muted">{op.module}</span>}
          </div>
          {op.grpc && <RoleStrip op={op} />}
          {!op.grpc && op.path.startsWith("/v1/deployments/") && (
            <>
              <Field label={t("Deployment")}>
                <IdPicker list="ServingService.ListDeployments" workspaceId={workspaceId} value={deploymentId}
                  onChange={(id) => { setDeploymentId(id); update({ ...body, slug: DeploymentSlugs.get(id) ?? body.slug ?? "" }); }} />
              </Field>
              <Field label={t("Deployment key")}>
                <input className="input font-mono text-xs" type="password" autoComplete="off" value={deploymentKey} onChange={(e) => setDeploymentKey(e.target.value)} />
              </Field>
            </>
          )}
          {method && <Tabs tabs={[{ key: "form", label: t("Form") }, { key: "json", label: "JSON" }]} value={tab}
            onChange={(k) => { if (k === "form") { const p = parsedRaw(); if (p) setBody(p); } else setRaw(JSON.stringify(body, null, 2)); setTab(k); }} />}
          {tab === "form" && method
            ? <MessageForm schema={method.input} value={body} onChange={update} refs={op.refs} workspaceId={workspaceId} />
            : <textarea className="input min-h-64 font-mono text-xs" value={raw} onChange={(e) => setRaw(e.target.value)} spellCheck={false} />}
          {isWrite && targets.length > 0 && (
            <div className="flex flex-wrap gap-1.5">{targets.map(([k, v]) => <HardwareChip key={k}>{k}: {String(v).slice(0, 8)}</HardwareChip>)}</div>
          )}
          <div className="flex flex-wrap items-center gap-2">
            {busy && op.streaming
              ? <button className="btn-secondary" onClick={() => abort.current?.abort()}><Square className="h-4 w-4" />{t("Stop")}</button>
              : <button className={armed ? "btn-danger" : "btn-primary"} disabled={busy || blocked} onClick={onSend}>
                  {blocked ? <ShieldAlert className="h-4 w-4" /> : <Play className="h-4 w-4" />}{armed ? t("Send for real") : t("Send")}
                </button>}
            {armed && <button className="btn-ghost" onClick={() => setArmed(false)}>{t("Cancel")}</button>}
            {op.path.endsWith("/predict") && deploymentId && <button className="btn-secondary" onClick={() => setTesting(true)}><FlaskConical className="h-4 w-4" />{t("Test with images")}</button>}
          </div>
          <Snippets op={op} body={tab === "json" ? raw : JSON.stringify(body)} deploymentKey={deploymentKey} />
        </div>
        <div className="min-w-0">
          {(result || events.length > 0) && <ResponseView result={result ?? { ok: true, status: "…", ms: 0, events }}
            onUseId={(id) => update({ ...body, ...Object.fromEntries(Object.keys(body).filter((k) => k.endsWith("Id") && k !== "workspaceId" && !body[k]).slice(0, 1).map((k) => [k, id])) })} />}
        </div>
      </div>
      {confirmDelete && (
        <ConfirmDialog title={t("Send")} consequence={op.id} confirmText={op.grpc?.method ?? op.path} label={t("Send")}
          onClose={() => setConfirmDelete(false)} onConfirm={() => { setConfirmDelete(false); void send(); }} />
      )}
      {testing && deploymentId && (
        <ModelTestSheet ws={workspaceId} target={{ kind: "deployment", deploymentId, title: String(body.slug ?? ""), task: DeploymentSlugs.task(deploymentId) }} onClose={() => setTesting(false)} />
      )}
    </Dialog>
  );
}

function fillPath(path: string, body: JsonObject): string {
  return path.replace(/\{(\w+)\}/g, (_, name: string) => encodeURIComponent(String(body[name] ?? "")));
}
