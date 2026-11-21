import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Activity, Download, FlaskConical, Play, Plus, Sparkles, Square, Trash2 } from "lucide-react";
import { api, errorText } from "../lib/api";
import { CommandBlock, ConfirmDialog, Dialog, EmptyState, ErrorBox, Field, PageHeader, RelTime, StateBadge } from "../components/ui";
import { DataTable } from "../components/DataTable";
import { translate as t } from "../i18n";
import { useSticky } from "../lib/persist";
import { ModelTestSheet, type TestTarget } from "../components/ModelTester";
import type { Deployment } from "../gen/mlplatform/v1/serving_pb";

const RUNNING = ["creating", "deploying", "healthy", "degraded"];
const endpointOf = (d: Deployment) => (d.publicUrl ? d.publicUrl + "/predict" : window.location.origin + d.predictUrl);
const gpuText = (g: number) => (g < 0 ? t("by recipe") : String(g));

/** Deployments: list, deploy, and one sheet per deployment (start/stop, change version, keys, delete). */
export function DeploymentsPage({ ws, onMonitor, onLlm }: { ws: string; onMonitor: (id: string) => void; onLlm: (id: string) => void }) {
  const qc = useQueryClient();
  const deps = useQuery({ queryKey: ["deployments", ws], queryFn: () => api.serving.listDeployments({ workspaceId: ws }), refetchInterval: 4000 });
  const refresh = () => qc.invalidateQueries({ queryKey: ["deployments", ws] });
  const [creating, setCreating] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const [test, setTest] = useState<TestTarget | null>(null);
  const stop = useMutation({ mutationFn: (id: string) => api.serving.stopDeployment({ workspaceId: ws, deploymentId: id }), onSuccess: refresh });
  const start = useMutation({ mutationFn: (id: string) => api.serving.startDeployment({ workspaceId: ws, deploymentId: id, modelVersionId: "" }), onSuccess: refresh });
  const all = deps.data?.deployments ?? [];
  const [showStopped, setShowStopped] = useSticky("deployments:stopped", true);
  const stoppedCount = all.filter((d) => d.state === "stopped").length;
  const list = showStopped ? all : all.filter((d) => d.state !== "stopped");
  const opened = all.find((d) => d.id === openId);
  return (
    <>
      <PageHeader title="Deployments"
        actions={<>
          {stoppedCount > 0 && <button className="btn-ghost" onClick={() => setShowStopped(!showStopped)}>{showStopped ? t("Hide stopped") : t("Show stopped ({n})", { n: stoppedCount })}</button>}
          <button className="btn-primary" onClick={() => setCreating(true)}><Plus className="h-4 w-4" />Deploy</button>
        </>} />
      <ErrorBox error={deps.error ? errorText(deps.error) : stop.error ? errorText(stop.error) : start.error ? errorText(start.error) : null} />
      {list.length === 0 ? <EmptyState title={all.length ? t("No running deployments") : t("No deployments yet")} /> : (
        <DataTable rows={list} loading={deps.isLoading} rowKey={(d) => d.id} onRowClick={(d) => setOpenId(d.id)}
          columns={[
            { key: "slug", header: t("Name"), value: (d) => d.slug, cell: (d) => <span className="font-medium">{d.slug}{d.purpose === "test" && <span className="badge ml-2 bg-sky-100 text-sky-700">{t("test")}</span>}</span> },
            { key: "model", header: "Model", value: (d) => d.modelName, cell: (d) => <span>{d.modelName} <span className="text-fg-subtle">v{d.modelVersion}</span></span> },
            { key: "task", header: "Task", value: (d) => d.task, cell: (d) => <span className="text-fg-muted">{d.task}</span> },
            { key: "state", header: t("Status"), value: (d) => d.state, cell: (d) => (
              <span className="block max-w-xs">
                <StateBadge state={d.state} reason={d.stateReason || undefined} />
                {d.stateReason && d.state !== "healthy" && d.state !== "stopped" && <span className="mt-0.5 block truncate text-xs text-fg-muted" title={t(d.stateReason)}>{t(d.stateReason)}</span>}
              </span>) },
            { key: "created", header: t("Created"), value: (d) => Number(d.createdAt?.seconds ?? 0), cell: (d) => <span className="text-xs"><RelTime at={d.createdAt} /></span> },
            { key: "act", header: "", align: "right", cell: (d) => (
              <span className="inline-flex gap-1 whitespace-nowrap" onClick={(e) => e.stopPropagation()}>
                {d.state === "healthy" && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setTest({ kind: "deployment", deploymentId: d.id, title: d.slug, task: d.task })}>
                  <FlaskConical className="h-3.5 w-3.5" />{t("Test")}</button>}
                {RUNNING.includes(d.state)
                  ? <button className="btn-ghost h-7 px-2 text-xs" disabled={stop.isPending} onClick={() => stop.mutate(d.id)}><Square className="h-3.5 w-3.5" />{t("Stop")}</button>
                  : <button className="btn-ghost h-7 px-2 text-xs" disabled={start.isPending} onClick={() => start.mutate(d.id)}><Play className="h-3.5 w-3.5" />{t("Start")}</button>}
              </span>) },
          ]} />)}
      {opened && <DeploymentSheet ws={ws} d={opened} onClose={() => setOpenId(null)} onMonitor={() => onMonitor(opened.id)} onLlm={() => onLlm(opened.id)}
        onTest={() => setTest({ kind: "deployment", deploymentId: opened.id, title: opened.slug, task: opened.task })} refresh={refresh} />}
      {test && <ModelTestSheet ws={ws} target={test} onClose={() => setTest(null)} />}
      {creating && <DeploySheet ws={ws} onClose={() => setCreating(false)} onDone={(id) => { refresh(); setCreating(false); setOpenId(id); }} />}
    </>
  );
}

function DeploySheet({ ws, onClose, onDone }: { ws: string; onClose: () => void; onDone: (id: string) => void }) {
  const models = useQuery({ queryKey: ["models", ws], queryFn: () => api.registry.listModels({ workspaceId: ws }) });
  const [modelId, setModelId] = useState("");
  const versions = useQuery({ queryKey: ["mversions", ws, modelId], queryFn: () => api.registry.listModelVersions({ workspaceId: ws, modelId }), enabled: !!modelId });
  const [f, setF] = useState({ slug: "", versionId: "", gpus: -1 });
  const slugOf = (name: string, v: number) => `${name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 40)}-v${v}`;
  const create = useMutation({
    mutationFn: () => api.serving.createDeployment({ workspaceId: ws, slug: f.slug, modelVersionId: f.versionId, cpus: 1, gpus: f.gpus, imageOverride: "" }),
    onSuccess: (d) => onDone(d.id),
  });
  const model = (models.data?.models ?? []).find((m) => m.id === modelId);
  return (
    <Dialog title={t("Deploy model version")} onClose={onClose}>
      <div className="space-y-3">
        <Field label="Model">
          <select className="input" value={modelId} onChange={(e) => { setModelId(e.target.value); setF({ ...f, versionId: "" }); }}>
            <option value="">{t("— pick a model —")}</option>
            {(models.data?.models ?? []).map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
          </select>
        </Field>
        <Field label={t("Version")}>
          <select className="input" value={f.versionId} onChange={(e) => {
            const v = (versions.data?.versions ?? []).find((x) => x.id === e.target.value);
            setF({ ...f, versionId: e.target.value, slug: f.slug || (v && model ? slugOf(model.name, v.version) : "") });
          }}>
            <option value="">{t("— pick a version —")}</option>
            {(versions.data?.versions ?? []).map((v) => <option key={v.id} value={v.id}>v{v.version}{v.stage && v.stage !== "none" ? ` · ${t(v.stage)}` : ""}</option>)}
          </select>
        </Field>
        <Field label={t("Endpoint name")}><input className="input" value={f.slug} onChange={(e) => setF({ ...f, slug: e.target.value })} /></Field>
        <Field label="GPU">
          <select className="input" value={f.gpus} onChange={(e) => setF({ ...f, gpus: Number(e.target.value) })}>
            <option value={-1}>{t("by recipe")}</option><option value={0}>0</option><option value={1}>1</option>
          </select>
        </Field>
        <ErrorBox error={create.error ? errorText(create.error) : null} />
        <button className="btn-primary" disabled={!f.versionId || !f.slug || create.isPending} onClick={() => create.mutate()}>Deploy</button>
      </div>
    </Dialog>
  );
}

function DeploymentSheet({ ws, d, onClose, onMonitor, onLlm, onTest, refresh }:
  { ws: string; d: Deployment; onClose: () => void; onMonitor: () => void; onLlm: () => void; onTest: () => void; refresh: () => void }) {
  const qc = useQueryClient();
  const running = RUNNING.includes(d.state);
  const models = useQuery({ queryKey: ["models", ws], queryFn: () => api.registry.listModels({ workspaceId: ws }) });
  const modelId = (models.data?.models ?? []).find((m) => m.name === d.modelName)?.id ?? "";
  const versions = useQuery({ queryKey: ["mversions", ws, modelId], queryFn: () => api.registry.listModelVersions({ workspaceId: ws, modelId }), enabled: !!modelId && !running });
  const [versionId, setVersionId] = useState(d.modelVersionId);
  const keys = useQuery({ queryKey: ["dep-keys", ws, d.id], queryFn: () => api.serving.listEndpointKeys({ workspaceId: ws, deploymentId: d.id }) });
  const refreshKeys = () => qc.invalidateQueries({ queryKey: ["dep-keys", ws, d.id] });
  const [keyName, setKeyName] = useState("");
  const [newKey, setNewKey] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [lang, setLang] = useSticky<Lang>("deployments:lang", "python");
  const stop = useMutation({ mutationFn: () => api.serving.stopDeployment({ workspaceId: ws, deploymentId: d.id }), onSuccess: refresh });
  const start = useMutation({ mutationFn: () => api.serving.startDeployment({ workspaceId: ws, deploymentId: d.id, modelVersionId: versionId }), onSuccess: refresh });
  const del = useMutation({ mutationFn: () => api.serving.deleteDeployment({ workspaceId: ws, deploymentId: d.id }), onSuccess: () => { refresh(); onClose(); } });
  const mkKey = useMutation({
    mutationFn: () => api.serving.createEndpointKey({ workspaceId: ws, deploymentId: d.id, name: keyName.trim() || "default" }),
    onSuccess: (k) => { setNewKey(k.key); setKeyName(""); refreshKeys(); },
  });
  const revoke = useMutation({ mutationFn: (keyId: string) => api.serving.revokeEndpointKey({ workspaceId: ws, deploymentId: d.id, keyId }), onSuccess: refreshKeys });
  const err = stop.error ?? start.error ?? del.error ?? mkKey.error ?? revoke.error ?? keys.error;
  return (
    <Dialog title={d.slug} onClose={onClose} wide>
      <div className="space-y-5 text-sm">
        <div className="grid grid-cols-[8rem_1fr] gap-y-1.5">
          <span className="text-fg-muted">{t("Status")}</span>
          <span><StateBadge state={d.state} reason={d.stateReason || undefined} />{d.stateReason && d.state !== "healthy" && <span className="ml-2 text-xs text-fg-muted">{t(d.stateReason)}</span>}</span>
          <span className="text-fg-muted">Model</span><span>{d.modelName} <span className="text-fg-subtle">v{d.modelVersion}</span> · {d.task}</span>
          <span className="text-fg-muted">GPU</span><span>{gpuText(d.gpus)}</span>
          <span className="text-fg-muted">{t("Created")}</span><span><RelTime at={d.createdAt} /></span>
        </div>

        <div className="flex flex-wrap gap-2">
          {d.state === "healthy" && <button className="btn-primary" onClick={onTest}><FlaskConical className="h-4 w-4" />{t("Test")}</button>}
          <button className="btn-ghost" onClick={onMonitor}><Activity className="h-4 w-4" />{t("Monitor")}</button>
          {d.task === "generate" && <button className="btn-ghost" onClick={onLlm}><Sparkles className="h-4 w-4" />Prompt · Guardrail · Eval</button>}
          {running && <button className="btn-ghost" disabled={stop.isPending} onClick={() => stop.mutate()}><Square className="h-4 w-4" />{t("Stop")}</button>}
          {!running && <button className="btn-ghost text-danger" onClick={() => setConfirmDelete(true)}><Trash2 className="h-4 w-4" />{t("Delete")}</button>}
        </div>

        {!running && (
          <section className="space-y-2">
            <div className="font-medium">{t("Start again")}</div>
            <div className="flex gap-2">
              <select className="input" value={versionId} onChange={(e) => setVersionId(e.target.value)}>
                {!(versions.data?.versions ?? []).some((v) => v.id === d.modelVersionId) && <option value={d.modelVersionId}>v{d.modelVersion}</option>}
                {(versions.data?.versions ?? []).map((v) => <option key={v.id} value={v.id}>v{v.version} · {v.stage}</option>)}
              </select>
              <button className="btn-primary shrink-0" disabled={start.isPending} onClick={() => start.mutate()}><Play className="h-4 w-4" />{t("Start")}</button>
            </div>
          </section>)}

        <section className="space-y-2">
          <div className="font-medium">Endpoint</div>
          <CommandBlock text={endpointOf(d)} />
        </section>

        <section className="space-y-2">
          <div className="font-medium">{t("Keys")}</div>
          <div className="flex gap-2">
            <input className="input" value={keyName} onChange={(e) => setKeyName(e.target.value)} placeholder={t("Key name")} />
            <button className="btn-ghost shrink-0" disabled={mkKey.isPending} onClick={() => mkKey.mutate()}><Plus className="h-4 w-4" />{t("Create key")}</button>
          </div>
          {(keys.data?.keys ?? []).length > 0 && (
            <table className="w-full text-sm">
              <tbody>{(keys.data?.keys ?? []).map((k) => (
                <tr key={k.id} className="border-b border-line last:border-0">
                  <td className="py-1.5 pr-2">{k.name}</td>
                  <td className="py-1.5 pr-2 font-mono text-xs text-fg-muted">{k.prefix}…</td>
                  <td className="py-1.5 pr-2 text-xs text-fg-muted"><RelTime at={k.createdAt} /></td>
                  <td className="py-1.5 text-right">{k.revokedAt
                    ? <span className="badge bg-surface-2 text-fg-muted">{t("revoked")}</span>
                    : <button className="btn-ghost h-7 px-2 text-xs text-danger" disabled={revoke.isPending} onClick={() => revoke.mutate(k.id)}>{t("Revoke")}</button>}</td>
                </tr>))}</tbody>
            </table>)}
        </section>

        <section className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-medium">{t("Use in your app")}</span>
            <span className="ml-auto inline-flex rounded-lg border border-line p-0.5 text-xs" role="tablist">
              {LANGS.map(([k, label]) => (
                <button key={k} type="button" role="tab" aria-selected={lang === k} onClick={() => setLang(k)}
                  className={`rounded-md px-2.5 py-1 ${lang === k ? "bg-primary/10 font-medium text-primary" : "text-fg-muted hover:text-fg"}`}>{label}</button>))}
            </span>
          </div>
          <CommandBlock text={snippet(lang, endpointOf(d), newKey ?? "mlpd_…", d.task)} />
          {lang !== "curl" && (
            <a className="btn-ghost inline-flex h-8 text-xs" href={lang === "python" ? "/sdk/mlplatform.py" : "/sdk/MLPlatformClient.cs"} download>
              <Download className="h-3.5 w-3.5" />{lang === "python" ? "mlplatform.py" : "MLPlatformClient.cs"}</a>)}
        </section>

        <ErrorBox error={err ? errorText(err) : null} />
      </div>
      {confirmDelete && (
        <ConfirmDialog title={t("Delete deployment {name}?", { name: d.slug })} label="Delete" confirmText={d.slug} busy={del.isPending}
          consequence={t("Keys and monitoring history are deleted.")} onConfirm={() => del.mutate()} onClose={() => setConfirmDelete(false)} />)}
    </Dialog>
  );
}

/** A request body that matches the deployment's task (the gateway accepts image_b64 | text | tabular | messages). */
function sampleCall(url: string, key: string, task: string): string {
  const head = `curl -X POST ${url} \\\n  -H "Authorization: Bearer ${key}" -H "Content-Type: application/json" \\\n  `;
  if (task === "generate") return head + `-d '{"messages":[{"role":"user","content":"Hello"}],"max_tokens":256}'`;
  if (task.startsWith("tabular")) return head + `-d '{"inputs":[{"tabular":{"feature_a":3,"feature_b":4}}]}'`;
  if (task === "zero-shot-classify") return head + `-d '{"inputs":[{"text":"The text to analyse","labels":["positive","negative"]}]}'`;
  if (["text-classify", "ner"].includes(task)) return head + `-d '{"inputs":[{"text":"The text to analyse"}]}'`;
  const labels = task === "zero-shot-detect" ? `,\\"labels\\":[\\"fire\\",\\"smoke\\"]` : "";
  return `IMG=$(base64 < image.jpg | tr -d '\\n')\n` + head + `-d "{\\"inputs\\":[{\\"image_b64\\":\\"$IMG\\",\\"conf\\":0.25${labels}}]}"`;
}

type Lang = "python" | "dotnet" | "curl";
const LANGS: [Lang, string][] = [["python", "Python"], ["dotnet", ".NET"], ["curl", "curl"]];

/** Code that calls the deployment with the SDK of the chosen language (or plain curl), for the deployment's task. */
function snippet(lang: Lang, url: string, key: string, task: string): string {
  if (lang === "curl") return sampleCall(url, key, task);
  const zero = task.startsWith("zero-shot"), tab = task.startsWith("tabular"), txt = ["text-classify", "ner", "zero-shot-classify"].includes(task);
  if (lang === "python") {
    const head = `from mlplatform import Client\n\nmodel = Client("${url}", "${key}")\n`;
    if (task === "generate") return head + `reply = model.chat([{"role": "user", "content": "Hello"}], max_tokens=256)\nprint(reply["text"])`;
    if (tab) return head + `print(model.tabular({"feature_a": 3, "feature_b": 4}))`;
    if (txt) return head + `print(model.text("The text to analyse"${zero ? `, labels=["positive", "negative"]` : ""}))`;
    if (task.includes("classify")) return head + `for x in model.classify("image.jpg"):\n    print(x["label"], x["score"])`;
    return head + `result = model.detect("image.jpg", conf=0.25${zero ? `, labels=["fire", "smoke"]` : ""})\nfor obj in result["objects"]:\n    print(obj["class"], obj["confidence"], obj["box"])`;
  }
  const head = `using MLPlatform.Client;\n\nusing var model = new MLPlatformClient("${url}", "${key}");\n`;
  if (task === "generate") return head + `var reply = await model.ChatAsync(new[] { new ChatMessage("user", "Hello") }, maxTokens: 256);\nConsole.WriteLine(reply.Text);`;
  if (tab) return head + `Console.WriteLine(await model.TabularAsync(new Dictionary<string, object?> { ["feature_a"] = 3, ["feature_b"] = 4 }));`;
  if (txt) return head + `Console.WriteLine(await model.TextAsync("The text to analyse"${zero ? `, labels: new[] { "positive", "negative" }` : ""}));`;
  if (task.includes("classify")) return head + `foreach (var x in await model.ClassifyAsync(File.ReadAllBytes("image.jpg")))\n    Console.WriteLine($"{x.Label} {x.Score:F2}");`;
  return head + `var result = await model.DetectAsync("image.jpg", conf: 0.25${zero ? `, labels: new[] { "fire", "smoke" }` : ""});\nforeach (var o in result.Objects)\n    Console.WriteLine($"{o.Class} {o.Confidence:F2} [{o.Box.X1}, {o.Box.Y1}, {o.Box.X2}, {o.Box.Y2}]");`;
}
