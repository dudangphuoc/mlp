import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { bytes } from "../lib/format";
import { EmptyState, ErrorBox, PageHeader, RelTime } from "../components/ui";
import { StageControl } from "../components/ModelStage";
import { DataTable } from "../components/DataTable";
import { HubImportDialog, HubImportsPanel } from "../components/HuggingFace";
import { translate as t } from "../i18n";
import { Download, FlaskConical, Gauge, Repeat2 } from "lucide-react";
import { ExportSheet, type ExportTarget } from "../components/ModelExport";
import { EvaluateSheet, type EvalTarget } from "../components/Evaluation";
import { ModelTestSheet, type TestTarget } from "../components/ModelTester";

/** S-11-1/2 Models + versions + stage transitions; import models from Hugging Face (M11 R6). */
export function ModelsPage({ ws, go }: { ws: string; go: (path: string) => void }) {
  const qc = useQueryClient();
  const models = useQuery({ queryKey: ["models", ws], queryFn: () => api.registry.listModels({ workspaceId: ws }), refetchInterval: 10000 });
  const [sel, setSel] = useState<string | null>(() => new URLSearchParams(window.location.search).get("model"));
  const [hub, setHub] = useState(false);
  const [test, setTest] = useState<TestTarget | null>(null);
  const [convert, setConvert] = useState<ExportTarget | null>(null);
  const [evaluate, setEvaluate] = useState<EvalTarget | null>(null);
  const versions = useQuery({ queryKey: ["mversions", ws, sel], queryFn: () => api.registry.listModelVersions({ workspaceId: ws, modelId: sel! }), enabled: !!sel });
  const pick = (id: string) => {
    setSel(id);
    const u = new URL(window.location.href); u.searchParams.set("model", id); window.history.replaceState(window.history.state, "", u);
  };
  const list = models.data?.models ?? [];
  const current = list.find((m) => m.id === sel);
  return (
    <>
      <PageHeader title="Models"
        actions={<button className="btn-primary" onClick={() => setHub(true)}><Download className="h-4 w-4" />{t("Import model")}</button>} />
      <ErrorBox error={models.error ? errorText(models.error) : null} />
      <HubImportsPanel ws={ws} kind="model" onOpen={() => void qc.invalidateQueries({ queryKey: ["models", ws] })} />
      {list.length === 0 && !models.isLoading ? (
        <EmptyState title={t("No models yet")} action={<button className="btn-primary" onClick={() => setHub(true)}>{t("Import from Hugging Face")}</button>} />
      ) : (
        <div className="space-y-5">
          <DataTable rows={list} loading={models.isLoading} rowKey={(m) => m.id} onRowClick={(m) => pick(m.id)} pageSize={20}
            columns={[
              { key: "name", header: "Model", value: (m) => m.name, cell: (m) => <span className={`font-medium ${m.id === sel ? "text-primary" : ""}`}>{m.name}</span> },
              { key: "task", header: "Task", value: (m) => m.task, cell: (m) => <span className="text-fg-muted">{m.task}</span> },
              { key: "latest", header: t("Latest version"), align: "right", value: (m) => m.latestVersion, cell: (m) => `v${m.latestVersion}` },
              { key: "prod", header: "Production", align: "right", value: (m) => m.productionVersion,
                cell: (m) => (m.productionVersion ? <span className="badge bg-emerald-100 text-emerald-700">v{m.productionVersion}</span> : <span className="text-fg-subtle">—</span>) },
              { key: "created", header: t("Created"), value: (m) => Number(m.createdAt?.seconds ?? 0), cell: (m) => <RelTime at={m.createdAt} /> },
            ]} />
          {current && (
            <section>
              <h2 className="mb-2 text-sm font-semibold">{t("Versions of {name}", { name: current.name })}</h2>
              <DataTable rows={versions.data?.versions ?? []} loading={versions.isLoading} rowKey={(v) => v.id} search={false} urlKey="v"
                columns={[
                  { key: "v", header: "Version", value: (v) => v.version, cell: (v) => <span className="font-medium">v{v.version}</span> },
                  { key: "source", header: t("Source"), value: (v) => v.source, cell: (v) => v.source === "hf_hub" && v.sourceRef.startsWith("hf://")
                      ? <a className="mono text-xs text-primary hover:underline" href={`https://huggingface.co/${v.sourceRef.replace(/^hf:\/\//, "").split("@")[0]}`} target="_blank" rel="noreferrer">🤗 {v.sourceRef.replace(/^hf:\/\//, "")}</a>
                      : <span className="text-xs text-fg-muted">{v.source === "run" ? "run" : v.source}</span> },
                  { key: "storage", header: t("Storage"), value: (v) => v.storage, cell: (v) => <span className={`badge ${v.storage === "reference" ? "bg-sky-100 text-sky-700" : "bg-surface-2 text-fg-muted"}`}>{v.storage === "reference" ? t("reference") : t("in storage")}</span> },
                  { key: "recipe", header: "Recipe", value: (v) => v.recipe, cell: (v) => <span className="mono text-xs">{v.recipe}/{v.variant}</span> },
                  { key: "stage", header: "Stage", value: (v) => v.stage, cell: (v) => <StageControl ws={ws} version={v} /> },
                  { key: "metrics", header: "Metric", cell: (v) => <div className="mono text-xs">{Object.entries(JSON.parse(v.metricsJson || "{}") as Record<string, number>)
                      .filter(([k]) => k.startsWith("val/") || k.startsWith("test/")).slice(0, 3).map(([k, x]) => <div key={k}>{k}={Number(x).toFixed(4)}</div>)}</div> },
                  { key: "files", header: "File", align: "right", value: (v) => Number(v.sizeBytes), cell: (v) => <span className="text-xs">{String(v.fileCount)} · {bytes(v.sizeBytes)}</span> },
                  { key: "created", header: t("Created"), value: (v) => Number(v.createdAt?.seconds ?? 0), cell: (v) => <span className="text-xs"><RelTime at={v.createdAt} /></span> },
                  { key: "act", header: "", align: "right", cell: (v) => (
                    <span className="inline-flex gap-1 whitespace-nowrap">
                      <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setTest({ kind: "version", versionId: v.id, title: `${current.name} v${v.version}`, task: v.task || current.task })}>
                        <FlaskConical className="h-3.5 w-3.5" />{t("Test")}</button>
                      <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setEvaluate({ versionId: v.id, title: `${current.name} v${v.version}`, task: v.task || current.task })}>
                        <Gauge className="h-3.5 w-3.5" />{t("Evaluate")}</button>
                      <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setConvert({ versionId: v.id, title: `${current.name} v${v.version}` })}>
                        <Repeat2 className="h-3.5 w-3.5" />{t("Convert")}</button>
                      {["detect", "classify"].includes(v.task || current.task) && v.source !== "optimize" && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => go(`optimize?version=${v.id}`)}>
                        <Gauge className="h-3.5 w-3.5" />{t("Optimize")}</button>}
                    </span>) },
                ]} />
            </section>)}
        </div>)}
      {evaluate && <EvaluateSheet ws={ws} target={evaluate} onClose={() => setEvaluate(null)} />}
      {test && <ModelTestSheet ws={ws} target={test} onClose={() => setTest(null)} />}
      {convert && <ExportSheet ws={ws} target={convert} onClose={() => setConvert(null)} onJob={(id) => go(`jobs/${id}`)} />}
      {hub && <HubImportDialog ws={ws} kind="model" onClose={() => setHub(false)} />}
    </>
  );
}
