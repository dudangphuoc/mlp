import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { toJsonString } from "@bufbuild/protobuf";
import { Camera, CameraOff, FlaskConical, ImagePlus, Loader2, Play, Power, Trash2, Upload } from "lucide-react";
import { api, errorText } from "../lib/api";
import { Dialog, ErrorBox, Field, StateBadge } from "./ui";
import { translate as t } from "../i18n";
import { TaskOutputSchema, type TaskInput, type TaskOutput } from "../gen/mlplatform/v1/model_server_pb";

/** What to test: a model version (a replica is reused or started) or an existing deployment. */
export type TestTarget =
  | { kind: "version"; versionId: string; title: string; task: string }
  | { kind: "deployment"; deploymentId: string; title: string; task: string };

type Sample = {
  id: number;
  name: string;
  preview?: string;          // object URL of the image that was actually sent
  text?: string;
  busy: boolean;
  error?: string;
  outputs?: TaskOutput[];
  latencyMs?: number;
};

const PALETTE = ["#f43f5e", "#3b82f6", "#22c55e", "#f59e0b", "#a855f7", "#14b8a6", "#ec4899", "#eab308", "#06b6d4", "#84cc16"];
const color = (i: number) => PALETTE[Math.abs(i) % PALETTE.length];
const MAX_SIDE = 1600;         // images are downscaled in the browser: keeps each call well under the 4 MB gRPC limit
const MAX_AUDIO = 3.5 * 1024 * 1024;

/** Re-encode an image as JPEG with its longest side ≤ MAX_SIDE. Boxes come back in the pixels of this image. */
async function prepareImage(file: Blob): Promise<{ bytes: Uint8Array; url: string }> {
  const bmp = await createImageBitmap(file);
  const scale = Math.min(1, MAX_SIDE / Math.max(bmp.width, bmp.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(bmp.width * scale);
  canvas.height = Math.round(bmp.height * scale);
  canvas.getContext("2d")!.drawImage(bmp, 0, 0, canvas.width, canvas.height);
  const blob = await new Promise<Blob>((ok, fail) => canvas.toBlob((b) => (b ? ok(b) : fail(new Error("encode failed"))), "image/jpeg", 0.92));
  return { bytes: new Uint8Array(await blob.arrayBuffer()), url: URL.createObjectURL(blob) };
}

/** S-11-5 Model playground: try a model on your own inputs right after training (right sheet). */
export function ModelTestSheet({ ws, target, onClose }: { ws: string; target: TestTarget; onClose: () => void }) {
  const tasks = useQuery({ queryKey: ["tasks"], queryFn: () => api.catalog.listTasks({}) });
  const modality = tasks.data?.tasks.find((x) => x.key === target.task)?.modalityIn
    ?? (target.task.startsWith("tabular") ? "tabular" : target.task === "asr" || target.task.startsWith("audio") ? "audio" : "image");
  const zeroShot = target.task.startsWith("zero-shot");
  const boxes = ["detect", "segment", "obb", "pose", "zero-shot-detect"].includes(target.task);

  // Replica: given, or reused/started for the version.
  const [depId, setDepId] = useState(target.kind === "deployment" ? target.deploymentId : "");
  const start = useMutation({
    mutationFn: () => api.serving.startTest({ workspaceId: ws, modelVersionId: target.kind === "version" ? target.versionId : "", gpus: -1, imageOverride: "" }),
    onSuccess: (d) => setDepId(d.id),
  });
  const started = useRef(false);
  useEffect(() => {
    if (target.kind === "version" && !started.current) { started.current = true; start.mutate(); }
  }, [target.kind]); // eslint-disable-line react-hooks/exhaustive-deps
  const dep = useQuery({
    queryKey: ["deployment", ws, depId], enabled: !!depId,
    queryFn: () => api.serving.getDeployment({ workspaceId: ws, deploymentId: depId }),
    refetchInterval: (q) => (q.state.data?.state === "healthy" ? 15000 : 2000),
  });
  const d = dep.data;
  const ready = d?.state === "healthy";
  const stop = useMutation({ mutationFn: () => api.serving.stopDeployment({ workspaceId: ws, deploymentId: depId }), onSuccess: () => void dep.refetch() });

  // Inputs and results.
  const [samples, setSamples] = useState<Sample[]>([]);
  const [conf, setConf] = useState(0.25);
  const [source, setSource] = useState<"upload" | "camera">("upload");
  const [labels, setLabels] = useState("");
  const [text, setText] = useState("");
  const [tabular, setTabular] = useState("{\n  \n}");
  const [showRaw, setShowRaw] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const nextId = useRef(1);
  const update = (id: number, patch: Partial<Sample>) => setSamples((xs) => xs.map((s) => (s.id === id ? { ...s, ...patch } : s)));
  const labelList = labels.split(",").map((s) => s.trim()).filter(Boolean);

  async function run(name: string, input: TaskInput, extra: Partial<Sample> = {}) {
    const id = nextId.current++;
    setSamples((xs) => [{ id, name, busy: true, ...extra }, ...xs]);
    try {
      const r = await api.serving.tryPredict({ workspaceId: ws, deploymentId: depId, inputs: [input] });
      update(id, { busy: false, outputs: r.outputs, latencyMs: r.latencyMs });
    } catch (e) {
      update(id, { busy: false, error: errorText(e) });
    }
  }

  async function addFiles(files: FileList | File[]) {
    for (const f of Array.from(files)) {
      if (modality === "image") {
        if (!f.type.startsWith("image/")) continue;
        const img = await prepareImage(f);
        // Ask for low-confidence results too; the threshold slider filters them instantly in the browser.
        await run(f.name, { kind: { case: "image", value: { data: img.bytes, conf: boxes ? 0.05 : conf, iou: 0, imgsz: 0, labels: labelList } } } as TaskInput, { preview: img.url });
      } else if (modality === "audio") {
        if (f.size > MAX_AUDIO) {
          const id = nextId.current++;
          setSamples((xs) => [{ id, name: f.name, busy: false, error: t("Audio file is larger than {mb} MB", { mb: 3.5 }) }, ...xs]);
          continue;
        }
        const ext = f.name.split(".").pop()?.toLowerCase() ?? "wav";
        await run(f.name, { kind: { case: "audio", value: { data: new Uint8Array(await f.arrayBuffer()), format: ext, language: "" } } } as TaskInput,
          { preview: URL.createObjectURL(f) });
      }
    }
  }

  function runText() {
    const s = text.trim();
    if (!s) return;
    const input = target.task === "generate"
      ? { kind: { case: "chat", value: { messages: [{ role: "user", content: s }], maxTokens: 512, temperature: 0.7 } } }
      : { kind: { case: "text", value: { text: s, labels: labelList } } };
    void run(s.length > 60 ? s.slice(0, 60) + "…" : s, input as TaskInput, { text: s });
  }

  function runTabular() {
    let row: Record<string, unknown>;
    try { row = JSON.parse(tabular) as Record<string, unknown>; } catch { setSamples((xs) => [{ id: nextId.current++, name: "JSON", busy: false, error: t("Input is not valid JSON") }, ...xs]); return; }
    const numbers: Record<string, number> = {};
    const strings: Record<string, string> = {};
    for (const [k, v] of Object.entries(row)) if (typeof v === "number") numbers[k] = v; else strings[k] = String(v);
    void run(Object.keys(row).slice(0, 4).join(", ") || "{}", { kind: { case: "tabular", value: { numbers, strings } } } as TaskInput);
  }

  // Paste images from the clipboard.
  useEffect(() => {
    if (!ready || modality !== "image" || source !== "upload") return;
    const onPaste = (e: ClipboardEvent) => {
      const files = Array.from(e.clipboardData?.files ?? []).filter((f) => f.type.startsWith("image/"));
      if (files.length) void addFiles(files);
    };
    window.addEventListener("paste", onPaste);
    return () => window.removeEventListener("paste", onPaste);
  }); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <Dialog title={t("Test {name}", { name: target.title })} onClose={onClose} xwide>
      <div className="space-y-4">
        {/* Replica status */}
        <div className="flex flex-wrap items-center gap-3 rounded-lg border border-line bg-surface-2 px-3 py-2 text-sm">
          <FlaskConical className="h-4 w-4 text-primary" />
          {!d && <span className="inline-flex items-center gap-2 text-fg-muted"><Loader2 className="h-4 w-4 animate-spin" />{t("Preparing a replica…")}</span>}
          {d && <>
            <span className="font-medium">{d.slug}</span>
            <StateBadge state={d.state} reason={d.stateReason || undefined} />
            {!ready && d.state !== "failed" && d.state !== "stopped" && (d.stateReason.startsWith("Waiting for")
              ? <span className="inline-flex items-center gap-2 text-amber-500"><Loader2 className="h-4 w-4 animate-spin" />{t(d.stateReason)}</span>
              : <span className="inline-flex items-center gap-2 text-fg-muted"><Loader2 className="h-4 w-4 animate-spin" />
                  {t("Starting: the machine downloads the model (and builds the recipe image the first time). This can take a few minutes.")}</span>)}
            {d.purpose === "test" && ready && <span className="text-xs text-fg-subtle">{t("Test replica — stops after {n} min without calls", { n: d.idleStopMin })}</span>}
            {d.purpose === "test" && d.state !== "stopped" && (
              <button className="btn-ghost ml-auto h-7 px-2 text-xs" onClick={() => stop.mutate()}><Power className="h-3.5 w-3.5" />{t("Stop replica")}</button>)}
          </>}
        </div>
        <ErrorBox error={start.error ? errorText(start.error) : stop.error ? errorText(stop.error) : d?.state === "failed" ? t(d.stateReason || "Replica failed") : null} />

        {/* Inputs */}
        {ready && (
          <div className="space-y-3">
            {zeroShot && (
              <Field label={t("Candidate labels")}>
                <input className="input" value={labels} onChange={(e) => setLabels(e.target.value)} placeholder="fire, smoke" />
              </Field>)}
            {modality === "image" && (
              <div className="inline-flex overflow-hidden rounded-md border border-line text-sm">
                <button className={`inline-flex items-center gap-1.5 px-3 py-1.5 ${source === "upload" ? "bg-surface-2 text-fg" : "text-fg-muted"}`} onClick={() => setSource("upload")}><ImagePlus className="h-4 w-4" />{t("Images")}</button>
                <button className={`inline-flex items-center gap-1.5 px-3 py-1.5 ${source === "camera" ? "bg-surface-2 text-fg" : "text-fg-muted"}`} onClick={() => setSource("camera")}><Camera className="h-4 w-4" />{t("Live camera")}</button>
              </div>)}
            {modality === "image" && source === "camera" && (
              <CameraTester ws={ws} depId={depId} boxes={boxes} conf={conf} labels={labelList}
                onSnapshot={(blob) => void addFiles([new File([blob], `camera-${new Date().toLocaleTimeString()}.jpg`, { type: "image/jpeg" })])} />)}
            {modality === "image" && source === "upload" && (
              <div
                className="flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-line px-4 py-8 text-sm text-fg-muted hover:border-primary hover:text-fg"
                onClick={() => fileRef.current?.click()}
                onDragOver={(e) => e.preventDefault()}
                onDrop={(e) => { e.preventDefault(); void addFiles(e.dataTransfer.files); }}>
                <ImagePlus className="h-6 w-6" />
                <span>{t("Drop images here, click to choose, or paste from the clipboard")}</span>
                <input ref={fileRef} type="file" accept="image/*" multiple hidden onChange={(e) => { if (e.target.files) void addFiles(e.target.files); e.target.value = ""; }} />
              </div>)}
            {modality === "audio" && (
              <div className="flex items-center gap-3">
                <button className="btn-ghost" onClick={() => fileRef.current?.click()}><Upload className="h-4 w-4" />{t("Choose audio files")}</button>
                <span className="text-xs text-fg-subtle">{t("wav, mp3, flac… up to {mb} MB", { mb: 3.5 })}</span>
                <input ref={fileRef} type="file" accept="audio/*" multiple hidden onChange={(e) => { if (e.target.files) void addFiles(e.target.files); e.target.value = ""; }} />
              </div>)}
            {modality === "text" && (
              <div className="space-y-2">
                <textarea className="input min-h-28" value={text} onChange={(e) => setText(e.target.value)}
                  placeholder={target.task === "generate" ? t("Write a prompt…") : t("Type or paste text…")}
                  onKeyDown={(e) => { if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) runText(); }} />
                <button className="btn-primary" disabled={!text.trim()} onClick={runText}><Play className="h-4 w-4" />{t("Run")} <span className="text-xs opacity-70">Ctrl+Enter</span></button>
              </div>)}
            {modality === "tabular" && (
              <div className="space-y-2">
                <Field label={t("One row as JSON")}>
                  <textarea className="input mono min-h-28 text-xs" value={tabular} onChange={(e) => setTabular(e.target.value)} />
                </Field>
                <button className="btn-primary" onClick={runTabular}><Play className="h-4 w-4" />{t("Run")}</button>
              </div>)}
            {boxes && (
              <div className="flex items-center gap-3 text-sm">
                <span className="text-fg-muted">{t("Confidence threshold")}</span>
                <input type="range" min={0.05} max={0.95} step={0.05} value={conf} onChange={(e) => setConf(Number(e.target.value))} className="w-48" />
                <span className="mono w-10 text-xs">{conf.toFixed(2)}</span>
              </div>)}
          </div>)}

        {/* Results */}
        {samples.length > 0 && (
          <div className="space-y-3">
            <div className="flex items-center justify-between text-xs text-fg-muted">
              <span>{t("{n} results", { n: samples.length })}</span>
              <span className="inline-flex items-center gap-3">
                <label className="inline-flex items-center gap-1"><input type="checkbox" checked={showRaw} onChange={(e) => setShowRaw(e.target.checked)} />{t("Raw output")}</label>
                <button className="inline-flex items-center gap-1 hover:text-fg" onClick={() => setSamples([])}><Trash2 className="h-3.5 w-3.5" />{t("Clear")}</button>
              </span>
            </div>
            {samples.map((s) => <SampleCard key={s.id} s={s} conf={conf} raw={showRaw} />)}
          </div>)}
      </div>
    </Dialog>
  );
}

function SampleCard({ s, conf, raw }: { s: Sample; conf: number; raw: boolean }) {
  return (
    <div className="rounded-lg border border-line bg-surface p-3">
      <div className="mb-2 flex items-center justify-between gap-2 text-xs">
        <span className="truncate font-medium">{s.name}</span>
        {s.busy ? <Loader2 className="h-4 w-4 animate-spin text-fg-muted" /> : s.latencyMs !== undefined && <span className="mono text-fg-subtle">{Math.round(s.latencyMs)} ms</span>}
      </div>
      {s.error && <ErrorBox error={s.error} />}
      {s.outputs?.map((o, i) => <OutputView key={i} o={o} preview={s.preview} text={s.text} conf={conf} />)}
      {!s.outputs && s.preview && s.busy && <img src={s.preview} alt={s.name} className="max-h-[60vh] rounded opacity-60" />}
      {raw && s.outputs && (
        <pre className="mono mt-2 max-h-64 overflow-auto rounded bg-surface-2 p-2 text-[11px]">{s.outputs.map((o) => toJsonString(TaskOutputSchema, o, { prettySpaces: 2 })).join("\n")}</pre>)}
    </div>
  );
}

function OutputView({ o, preview, text, conf }: { o: TaskOutput; preview?: string; text?: string; conf: number }) {
  switch (o.kind.case) {
    case "detect": return <DetectView out={o.kind.value} preview={preview} conf={conf} />;
    case "classify": return <Bars items={o.kind.value.labels.map((l) => ({ label: l.label, score: l.score }))} preview={preview} />;
    case "ner": return <NerView text={text ?? ""} entities={o.kind.value.entities} />;
    case "generate": return <div className="whitespace-pre-wrap text-sm">{o.kind.value.text}</div>;
    case "asr": return (
      <div className="space-y-2 text-sm">
        {preview && <audio controls src={preview} className="w-full" />}
        <div className="whitespace-pre-wrap">{o.kind.value.text}</div>
        {o.kind.value.segments.length > 0 && (
          <div className="space-y-0.5 text-xs text-fg-muted">
            {o.kind.value.segments.map((g, i) => <div key={i}><span className="mono text-fg-subtle">{g.start.toFixed(1)}–{g.end.toFixed(1)}s</span> {g.speaker && <b>{g.speaker}: </b>}{g.text}</div>)}
          </div>)}
      </div>);
    case "tabular": {
      const v = o.kind.value;
      return (
        <div className="space-y-2 text-sm">
          {v.hasValue ? <div><span className="text-fg-muted">{t("Predicted value")}: </span><span className="mono font-semibold">{v.value}</span></div>
            : <div><span className="text-fg-muted">{t("Predicted class")}: </span><span className="font-semibold">{v.label}</span></div>}
          {Object.keys(v.probabilities).length > 0 && <Bars items={Object.entries(v.probabilities).map(([label, score]) => ({ label, score }))} />}
        </div>);
    }
    default: return <div className="text-xs text-fg-muted">{t("The model returned no output for this input.")}</div>;
  }
}

function DetectView({ out, preview, conf }: { out: Extract<TaskOutput["kind"], { case: "detect" }>["value"]; preview?: string; conf: number }) {
  const [size, setSize] = useState<{ w: number; h: number } | null>(null);
  const objs = out.objects.filter((o) => o.score >= conf);
  const w = out.width || size?.w || 1;
  const h = out.height || size?.h || 1;
  const counts = useMemo(() => {
    const m = new Map<string, { n: number; id: number }>();
    for (const o of objs) { const k = o.className || String(o.classId); m.set(k, { n: (m.get(k)?.n ?? 0) + 1, id: o.classId }); }
    return [...m.entries()].sort((a, b) => b[1].n - a[1].n);
  }, [objs]);
  return (
    <div className="space-y-2">
      {preview && (
        <div className="relative inline-block max-w-full">
          <img src={preview} alt="" className="block max-h-[65vh] max-w-full rounded" onLoad={(e) => setSize({ w: e.currentTarget.naturalWidth, h: e.currentTarget.naturalHeight })} />
          <svg className="pointer-events-none absolute inset-0 h-full w-full" viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none">
            {objs.map((o, i) => {
              const c = color(o.classId);
              const lw = Math.max(2, w / 400);
              return (
                <g key={i}>
                  {o.polygon.length >= 6
                    ? <polygon points={o.polygon.join(" ")} fill={c} fillOpacity={0.25} stroke={c} strokeWidth={lw} />
                    : <rect x={o.x1} y={o.y1} width={o.x2 - o.x1} height={o.y2 - o.y1} fill="none" stroke={c} strokeWidth={lw} />}
                  <text x={o.x1} y={Math.max(o.y1 - lw * 2, w / 60)} fill={c} fontSize={Math.max(12, w / 60)} fontWeight={600} paintOrder="stroke" stroke="#000" strokeWidth={lw}>
                    {(o.className || o.classId) + " " + Math.round(o.score * 100) + "%"}
                  </text>
                </g>);
            })}
          </svg>
        </div>)}
      <div className="flex flex-wrap gap-2 text-xs">
        {counts.length === 0 && <span className="text-fg-muted">{t("No objects above the threshold.")}</span>}
        {counts.map(([name, v]) => (
          <span key={name} className="inline-flex items-center gap-1 rounded-full border border-line px-2 py-0.5">
            <span className="h-2 w-2 rounded-full" style={{ background: color(v.id) }} />{name} <b>{v.n}</b>
          </span>))}
        {out.objects.length > objs.length && <span className="text-fg-subtle">{t("{n} below the threshold hidden", { n: out.objects.length - objs.length })}</span>}
      </div>
    </div>
  );
}

function Bars({ items, preview }: { items: { label: string; score: number }[]; preview?: string }) {
  const top = [...items].sort((a, b) => b.score - a.score).slice(0, 8);
  return (
    <div className="flex flex-wrap items-start gap-4">
      {preview && <img src={preview} alt="" className="max-h-56 rounded" />}
      <div className="min-w-64 flex-1 space-y-1.5">
        {top.map((x, i) => (
          <div key={x.label} className="text-sm">
            <div className="flex justify-between"><span className={i === 0 ? "font-semibold" : ""}>{x.label}</span><span className="mono text-xs text-fg-muted">{(x.score * 100).toFixed(1)}%</span></div>
            <div className="h-1.5 rounded bg-surface-2"><div className="h-1.5 rounded" style={{ width: `${Math.max(1, x.score * 100)}%`, background: color(i) }} /></div>
          </div>))}
      </div>
    </div>
  );
}

function NerView({ text, entities }: { text: string; entities: { label: string; start: number; end: number; score: number }[] }) {
  const parts: React.ReactNode[] = [];
  let at = 0;
  const labels = [...new Set(entities.map((e) => e.label))];
  [...entities].sort((a, b) => a.start - b.start).forEach((e, i) => {
    if (e.start < at) return;
    parts.push(text.slice(at, e.start));
    parts.push(<mark key={i} className="rounded px-0.5" style={{ background: color(labels.indexOf(e.label)) + "40" }} title={`${e.label} ${(e.score * 100).toFixed(0)}%`}>
      {text.slice(e.start, e.end)}<sup className="ml-0.5 text-[10px] font-semibold">{e.label}</sup></mark>);
    at = e.end;
  });
  parts.push(text.slice(at));
  return <div className="whitespace-pre-wrap text-sm leading-7">{parts}</div>;
}

type Detected = { classId: number; className: string; score: number; x1: number; y1: number; x2: number; y2: number; polygon: number[] };

/**
 * Live test with the viewer's camera: frames are captured in the browser, sent one at a time (the next frame goes out when the
 * previous answer arrives, so the rate follows the model's speed) and the result is drawn over the video.
 * The camera needs a secure page (https or localhost); frames are not stored.
 */
function CameraTester({ ws, depId, boxes, conf, labels, onSnapshot }: {
  ws: string; depId: string; boxes: boolean; conf: number; labels: string[]; onSnapshot: (frame: Blob) => void;
}) {
  const video = useRef<HTMLVideoElement>(null);
  const canvas = useRef<HTMLCanvasElement | null>(null);
  const stream = useRef<MediaStream | null>(null);
  const [devices, setDevices] = useState<MediaDeviceInfo[]>([]);
  const [deviceId, setDeviceId] = useState("");
  const [on, setOn] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [frame, setFrame] = useState<{ w: number; h: number; objects: Detected[]; top: { label: string; score: number }[] } | null>(null);
  const [stats, setStats] = useState<{ ms: number; fps: number } | null>(null);
  const live = useRef({ conf, labels });
  live.current = { conf, labels };

  const stop = () => {
    stream.current?.getTracks().forEach((tr) => tr.stop());
    stream.current = null;
    setOn(false);
  };
  useEffect(() => stop, []);

  async function start(id: string) {
    setError(null);
    if (!navigator.mediaDevices?.getUserMedia) { setError(t("The camera needs a secure page: open the site over https or on localhost.")); return; }
    try {
      stop();
      const s = await navigator.mediaDevices.getUserMedia({ audio: false, video: id ? { deviceId: { exact: id } } : { facingMode: "environment", width: { ideal: 1280 }, height: { ideal: 720 } } });
      stream.current = s;
      if (video.current) { video.current.srcObject = s; await video.current.play(); }
      setDevices((await navigator.mediaDevices.enumerateDevices()).filter((d) => d.kind === "videoinput"));
      setDeviceId(s.getVideoTracks()[0]?.getSettings().deviceId ?? id);
      setOn(true);
    } catch (e) {
      const name = (e as DOMException).name;
      setError(name === "NotAllowedError" ? t("Camera permission was denied. Allow the camera for this site in the browser and try again.")
        : name === "NotFoundError" ? t("No camera found on this device.") : t("Could not start the camera: {msg}", { msg: (e as Error).message }));
    }
  }

  const grab = (maxSide: number, quality: number): Promise<Blob | null> => new Promise((resolve) => {
    const v = video.current;
    if (!v || v.videoWidth === 0) { resolve(null); return; }
    const c = canvas.current ?? (canvas.current = document.createElement("canvas"));
    const k = Math.min(1, maxSide / Math.max(v.videoWidth, v.videoHeight));
    c.width = Math.round(v.videoWidth * k); c.height = Math.round(v.videoHeight * k);
    c.getContext("2d")!.drawImage(v, 0, 0, c.width, c.height);
    c.toBlob((b) => resolve(b), "image/jpeg", quality);
  });

  // Inference loop: one request in flight at a time.
  useEffect(() => {
    if (!on || !depId) return;
    let alive = true;
    (async () => {
      let ema = 0;
      while (alive) {
        const blob = await grab(960, 0.8);
        if (!blob) { await new Promise((r) => setTimeout(r, 200)); continue; }
        const started = performance.now();
        try {
          const r = await api.serving.tryPredict({ workspaceId: ws, deploymentId: depId, inputs: [{ kind: { case: "image", value: {
            data: new Uint8Array(await blob.arrayBuffer()), conf: boxes ? 0.05 : 0, iou: 0, imgsz: 0, labels: live.current.labels } } } as TaskInput] });
          if (!alive) break;
          const o = r.outputs[0];
          const c = canvas.current!;
          setFrame({
            w: (o?.kind.case === "detect" && o.kind.value.width) || c.width, h: (o?.kind.case === "detect" && o.kind.value.height) || c.height,
            objects: o?.kind.case === "detect" ? (o.kind.value.objects as Detected[]) : [],
            top: o?.kind.case === "classify" ? [...o.kind.value.labels].sort((a, b) => b.score - a.score).slice(0, 5) : [],
          });
          const total = performance.now() - started;
          ema = ema === 0 ? total : ema * 0.7 + total * 0.3;
          setStats({ ms: Math.round(r.latencyMs), fps: Math.round((1000 / ema) * 10) / 10 });
          setError(null);
        } catch (e) {
          if (!alive) break;
          setError(errorText(e));
          await new Promise((r) => setTimeout(r, 1500));
        }
      }
    })();
    return () => { alive = false; };
  }, [on, depId, ws, boxes]); // eslint-disable-line react-hooks/exhaustive-deps

  const shown = frame?.objects.filter((o) => o.score >= conf) ?? [];
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        {!on
          ? <button className="btn-primary" onClick={() => void start(deviceId)}><Camera className="h-4 w-4" />{t("Start camera")}</button>
          : <button className="btn-ghost" onClick={stop}><CameraOff className="h-4 w-4" />{t("Stop camera")}</button>}
        {devices.length > 1 && (
          <select className="input h-9 w-auto max-w-64" value={deviceId} onChange={(e) => void start(e.target.value)} aria-label={t("Camera")}>
            {devices.map((d, i) => <option key={d.deviceId} value={d.deviceId}>{d.label || t("Camera {n}", { n: i + 1 })}</option>)}
          </select>)}
        {on && <button className="btn-ghost" onClick={async () => { const b = await grab(MAX_SIDE, 0.92); if (b) onSnapshot(b); }}><ImagePlus className="h-4 w-4" />{t("Keep this frame")}</button>}
        {on && stats && <span className="mono ml-auto text-xs text-fg-muted">{stats.fps} FPS · {t("model {ms} ms", { ms: stats.ms })}</span>}
      </div>
      <ErrorBox error={error} />
      <div className={on ? "relative inline-block max-w-full overflow-hidden rounded-lg bg-black" : "hidden"}>
        <video ref={video} playsInline muted className="block max-h-[62vh] max-w-full" />
        {frame && (
          <svg className="pointer-events-none absolute inset-0 h-full w-full" viewBox={`0 0 ${frame.w} ${frame.h}`} preserveAspectRatio="none">
            {shown.map((o, i) => {
              const c = color(o.classId);
              const lw = Math.max(2, frame.w / 400);
              return (
                <g key={i}>
                  {o.polygon.length >= 6
                    ? <polygon points={o.polygon.join(" ")} fill={c} fillOpacity={0.25} stroke={c} strokeWidth={lw} />
                    : <rect x={o.x1} y={o.y1} width={o.x2 - o.x1} height={o.y2 - o.y1} fill="none" stroke={c} strokeWidth={lw} />}
                  <text x={o.x1} y={Math.max(o.y1 - lw * 2, frame.w / 50)} fill={c} fontSize={Math.max(12, frame.w / 50)} fontWeight={600} paintOrder="stroke" stroke="#000" strokeWidth={lw}>
                    {(o.className || o.classId) + " " + Math.round(o.score * 100) + "%"}</text>
                </g>);
            })}
          </svg>)}
        {frame && frame.top.length > 0 && (
          <div className="absolute left-2 top-2 space-y-0.5 rounded-md bg-black/60 px-2 py-1 text-xs text-white">
            {frame.top.map((l, i) => <div key={l.label} className={i === 0 ? "font-semibold" : "opacity-80"}>{l.label} {(l.score * 100).toFixed(0)}%</div>)}
          </div>)}
      </div>
      {on && boxes && <div className="flex flex-wrap gap-2 text-xs text-fg-muted">
        {shown.length === 0 ? <span>{t("No objects above the threshold.")}</span> : Object.entries(shown.reduce<Record<string, number>>((m, o) => { const k = o.className || String(o.classId); m[k] = (m[k] ?? 0) + 1; return m; }, {}))
          .map(([k, n]) => <span key={k} className="rounded-full border border-line px-2 py-0.5">{k} <b>{n}</b></span>)}
      </div>}
    </div>
  );
}
