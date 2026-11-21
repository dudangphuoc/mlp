import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, Play, Trash2 } from "lucide-react";
import { api, errorText } from "../lib/api";
import { bytes } from "../lib/format";
import { Dialog, EmptyState, ErrorBox, Field, RelTime, StateBadge } from "./ui";
import { translate as t } from "../i18n";

const DERIVED = ["onnx-fp16", "openvino-fp32", "openvino-fp16", "tensorrt-fp16", "tensorrt-fp32"];
/** Format name and the runtime / platform it is for (labels only). */
const FORMAT_INFO: Record<string, { name: string; target: string }> = {
  onnx: { name: "ONNX", target: "ONNX Runtime · Triton · .NET" },
  "onnx-fp16": { name: "ONNX FP16", target: "ONNX Runtime CUDA · Triton" },
  "openvino-fp32": { name: "OpenVINO FP32", target: "Intel CPU" },
  "openvino-fp16": { name: "OpenVINO FP16", target: "Intel CPU · iGPU · NPU" },
  "tensorrt-fp16": { name: "TensorRT FP16", target: "NVIDIA GPU · DeepStream · Triton" },
  "tensorrt-fp32": { name: "TensorRT FP32", target: "NVIDIA GPU · DeepStream · Triton" },
  engine: { name: "TensorRT", target: "NVIDIA GPU" },
  torchscript: { name: "TorchScript", target: "PyTorch · LibTorch C++" },
  openvino: { name: "OpenVINO", target: "Intel CPU · iGPU · NPU" },
  coreml: { name: "Core ML", target: "iOS · macOS" },
  saved_model: { name: "TF SavedModel", target: "TensorFlow Serving" },
  litert: { name: "LiteRT (TFLite)", target: "Android · Edge" },
  ncnn: { name: "NCNN", target: "Android · ARM" },
  mnn: { name: "MNN", target: "Mobile · ARM" },
  paddle: { name: "Paddle", target: "Paddle Inference" },
  rknn: { name: "RKNN", target: "Rockchip NPU" },
  executorch: { name: "ExecuTorch", target: "On-device PyTorch" },
};

/** Image build failures carry the whole build log; keep the code and the last error line. */
function shortError(e: string) {
  if (e.length <= 300) return e;
  const code = /^[A-Z_]+:/.exec(e)?.[0] ?? "";
  const i = Math.max(e.lastIndexOf("ERROR"), e.lastIndexOf("error:"));
  return `${code} … ${e.slice(i > 0 ? i : e.length - 240).slice(0, 240)}`;
}

export interface ExportTarget { versionId: string; title: string }

/** Convert a model version to another format (what its recipe's export supports) and download the result. */
export function ExportSheet({ ws, target, onClose, onJob }: { ws: string; target: ExportTarget; onClose: () => void; onJob: (jobId: string) => void }) {
  const qc = useQueryClient();
  const key = ["exports", ws, target.versionId];
  const list = useQuery({
    queryKey: key, queryFn: () => api.registry.listExports({ workspaceId: ws, modelVersionId: target.versionId }),
    refetchInterval: (q) => (q.state.data?.exports.some((e) => e.state === "running") ? 3000 : false),
  });
  const formats = list.data?.formats ?? [];
  const [format, setFormat] = useState("");
  const chosen = format || formats[0] || "";
  const onnxReady = (list.data?.exports ?? []).some((e) => e.format === "onnx" && e.state === "ready");
  const map = useQuery({ queryKey: ["cluster-map", ws], queryFn: () => api.ops.getClusterMap({ workspaceId: ws }) });
  const gpuHosts = (map.data?.hosts ?? []).filter((h) => (h.state === "online" || h.state === "busy") && h.gpus.length > 0);
  const [hostId, setHostId] = useState("");
  const host = hostId || gpuHosts[0]?.id || "";
  const refresh = () => void qc.invalidateQueries({ queryKey: key });
  const start = useMutation({
    mutationFn: () => api.registry.startExport({ workspaceId: ws, modelVersionId: target.versionId, format: chosen, hostId: chosen.startsWith("tensorrt-") ? host : "" }),
    onSuccess: refresh,
  });
  const remove = useMutation({ mutationFn: (id: string) => api.registry.deleteExport({ workspaceId: ws, exportId: id }), onSuccess: refresh });
  const download = useMutation({
    mutationFn: (id: string) => api.registry.getExportDownload({ workspaceId: ws, exportId: id }),
    onSuccess: (r) => { window.location.assign(r.url); },
  });
  const err = list.error ?? start.error ?? remove.error ?? download.error;
  const rows = list.data?.exports ?? [];
  return (
    <Dialog title={`${t("Convert")} — ${target.title}`} onClose={onClose} wide>
      <div className="space-y-4 text-sm">
        <ErrorBox error={err ? errorText(err) : null} />
        {list.isLoading ? <div className="skeleton h-20" /> : formats.length === 0 ? <EmptyState bare title={t("This model's recipe has no conversion")} /> : (
          <div className="space-y-3">
            <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {formats.map((f) => {
                const info = FORMAT_INFO[f] ?? { name: f, target: "" };
                const needsOnnx = DERIVED.includes(f) && !onnxReady;
                return (
                  <button key={f} type="button" disabled={needsOnnx} aria-pressed={f === chosen} onClick={() => setFormat(f)}
                    className={`min-h-[56px] rounded-xl border px-3 py-2 text-left active:scale-[0.98] disabled:opacity-45 ${f === chosen ? "border-primary bg-primary/10" : "border-line bg-surface-2 hover:border-primary/50"}`}>
                    <div className="text-sm font-medium">{info.name}</div>
                    <div className="truncate text-xs text-fg-muted">{needsOnnx ? t("needs ONNX") : info.target}</div>
                  </button>);
              })}
            </div>
            <div className="flex flex-wrap items-end gap-2">
              {chosen.startsWith("tensorrt-") && (
                <Field label={t("GPU machine")}>
                  <select className="input h-11 w-64" value={host} onChange={(e) => setHostId(e.target.value)}>
                    {gpuHosts.map((h) => <option key={h.id} value={h.id}>{h.name} · {h.gpus.map((g) => g.name).join(", ")}</option>)}
                  </select>
                </Field>)}
              <button className="btn-primary h-11 px-5" disabled={!chosen || start.isPending || (chosen.startsWith("tensorrt-") && !host)} onClick={() => start.mutate()}><Play className="h-4 w-4" />{t("Convert")}</button>
            </div>
          </div>)}
        {rows.length > 0 && (
          <ul className="divide-y divide-line">
            {rows.map((e) => (
              <li key={e.id} className="flex flex-wrap items-start gap-x-3 gap-y-1.5 py-2.5">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2"><span className="font-mono text-xs">{e.format}</span><StateBadge state={e.state} />
                    <span className="text-xs text-fg-muted"><RelTime at={e.createdAt} /></span></div>
                  {e.state === "failed" && e.error && <div className="mt-1 break-words text-xs text-danger">{shortError(t(e.error))}</div>}
                  {e.state === "ready" && <div className="mt-1 break-all font-mono text-xs text-fg-muted">{e.fileName} · {bytes(e.sizeBytes)}</div>}
                </div>
                <span className="inline-flex shrink-0 gap-1">
                  {e.state === "ready" && <button className="btn-primary h-9 px-3 text-xs" disabled={download.isPending} onClick={() => download.mutate(e.id)}><Download className="h-3.5 w-3.5" />{t("Download")}</button>}
                  {e.state !== "ready" && e.jobId && <button className="btn-ghost h-9 px-3 text-xs" onClick={() => onJob(e.jobId)}>{t("Log")}</button>}
                  {e.state !== "running" && <button className="btn-ghost h-9 w-9 p-0 text-danger" aria-label={t("Delete")} disabled={remove.isPending} onClick={() => remove.mutate(e.id)}><Trash2 className="mx-auto h-3.5 w-3.5" /></button>}
                </span>
              </li>))}
          </ul>)}
      </div>
    </Dialog>
  );
}
