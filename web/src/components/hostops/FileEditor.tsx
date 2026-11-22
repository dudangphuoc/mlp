import { useEffect, useState } from "react";
import { api, errorText } from "../../lib/api";
import { bytes } from "../../lib/format";
import { ErrorBox, Skeleton } from "../ui";
import { translate as t } from "../../i18n";

const READ = 4 * 1024 * 1024, WRITE = 1024 * 1024, MAX = 20 * 1024 * 1024;

/** Text editor pane: reads in 4 MiB chunks, saves in 1 MiB chunks (first chunk truncates). */
export function FileEditor({ ws, hostId, path }: { ws: string; hostId: string; path: string }) {
  const [text, setText] = useState<string | null>(null);
  const [orig, setOrig] = useState("");
  const [state, setState] = useState<{ size: number; binary: boolean; sha: string } | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    (async () => {
      const first = await api.fleet.readHostFile({ workspaceId: ws, hostId, path, offset: 0n, length: READ });
      const size = Number(first.stat?.size ?? 0n);
      if (!first.stat?.isText || size > MAX) { setState({ size, binary: true, sha: "" }); return; }
      const parts = [first.data];
      for (let off = first.data.length; off < size; off += READ)
        parts.push((await api.fleet.readHostFile({ workspaceId: ws, hostId, path, offset: BigInt(off), length: READ })).data);
      const all = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
      parts.reduce((o, p) => { all.set(p, o); return o + p.length; }, 0);
      const s = new TextDecoder().decode(all);
      setText(s); setOrig(s); setState({ size, binary: false, sha: "" });
    })().catch((e) => setErr(errorText(e)));
  }, [ws, hostId, path]);

  const save = async () => {
    if (text === null) return;
    setSaving(true); setErr(null);
    try {
      const data = new TextEncoder().encode(text);
      let sha = "";
      for (let off = 0; off < Math.max(data.length, 1); off += WRITE) {
        const r = await api.fleet.writeHostFile({ workspaceId: ws, hostId, path, offset: BigInt(off), data: data.subarray(off, off + WRITE), truncate: off === 0 });
        sha = r.sha256;
      }
      setOrig(text); setState({ size: data.length, binary: false, sha });
    } catch (e) { setErr(errorText(e)); } finally { setSaving(false); }
  };

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex h-10 flex-none items-center gap-2 border-b border-line px-3">
        <span className="mono min-w-0 flex-1 truncate text-xs text-fg-muted">{path}{state ? ` · ${bytes(state.size)}` : ""}{state?.sha ? ` · ${state.sha.slice(0, 12)}` : ""}</span>
        {state && !state.binary && <button className="inline-flex h-8 flex-none items-center whitespace-nowrap rounded-md bg-primary px-3 text-sm text-white disabled:opacity-50"
          disabled={saving || text === orig} onClick={() => void save()}>{saving ? t("Saving…") : t("Save")}</button>}
      </div>
      <ErrorBox error={err} />
      {!state ? <Skeleton rows={8} /> : state.binary ? <div className="p-3 text-sm text-fg-muted">{t("Binary or larger than 20 MB")}</div> : (
        <textarea className="mono min-h-0 flex-1 resize-none bg-transparent p-3 text-xs leading-5 outline-none" spellCheck={false}
          aria-label={path} value={text ?? ""} onChange={(e) => setText(e.target.value)} />)}
    </div>
  );
}
