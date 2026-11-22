import { useEffect, useRef, useState } from "react";
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import "@xterm/xterm/css/xterm.css";
import { Field } from "../ui";
import { translate as t } from "../../i18n";

const enum Kind { Data = 0, Resize = 1, Exit = 2, Ping = 3 }
type Phase = "idle" | "connecting" | "open" | "closed" | "failed";

function frame(kind: Kind, payload: Uint8Array): Uint8Array {
  const b = new Uint8Array(payload.length + 1);
  b[0] = kind;
  b.set(payload, 1);
  return b;
}

function resizePayload(cols: number, rows: number): Uint8Array {
  const b = new Uint8Array(4);
  new DataView(b.buffer).setUint16(0, cols);
  new DataView(b.buffer).setUint16(2, rows);
  return b;
}

/** Interactive shell on the machine over the agent data channel; opening it needs a reason (audited). */
export function TerminalPanel({ ws, hostId }: { ws: string; hostId: string }) {
  const box = useRef<HTMLDivElement>(null);
  const [reason, setReason] = useState("");
  const [phase, setPhase] = useState<Phase>("idle");
  const [status, setStatus] = useState("");
  const [session, setSession] = useState(0);

  useEffect(() => {
    if (session === 0 || !box.current) return;
    const term = new Terminal({ fontFamily: "JetBrains Mono, ui-monospace, monospace", fontSize: 13, cursorBlink: true, theme: { background: "#0b0e13" } });
    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(box.current);
    fit.fit();
    const q = new URLSearchParams({ workspace_id: ws, reason, cols: String(term.cols), rows: String(term.rows) });
    const sock = new WebSocket(`${location.protocol === "https:" ? "wss" : "ws"}://${location.host}/api/hosts/${hostId}/terminal?${q}`);
    sock.binaryType = "arraybuffer";
    const enc = new TextEncoder();
    setPhase("connecting");
    sock.onopen = () => { setPhase("open"); term.focus(); };
    sock.onmessage = (e) => {
      const b = new Uint8Array(e.data as ArrayBuffer);
      if (b[0] === Kind.Data) term.write(b.subarray(1));
      else if (b[0] === Kind.Exit) setStatus(`exit ${new DataView(b.buffer, 1).getInt32(0)}`);
    };
    sock.onclose = (e) => setPhase((p) => (p === "connecting" && e.code !== 1000 ? "failed" : "closed"));
    const input = term.onData((d) => sock.readyState === WebSocket.OPEN && sock.send(frame(Kind.Data, enc.encode(d))));
    const resize = term.onResize(({ cols, rows }) => sock.readyState === WebSocket.OPEN && sock.send(frame(Kind.Resize, resizePayload(cols, rows))));
    const ro = new ResizeObserver(() => fit.fit());
    ro.observe(box.current);
    return () => { ro.disconnect(); input.dispose(); resize.dispose(); sock.close(); term.dispose(); };
  }, [session]); // eslint-disable-line react-hooks/exhaustive-deps

  if (session === 0) return (
    <form className="max-w-md space-y-3" onSubmit={(e) => { e.preventDefault(); setStatus(""); setSession((s) => s + 1); }}>
      <Field label={t("Reason")}><input className="input" value={reason} onChange={(e) => setReason(e.target.value)} autoFocus /></Field>
      <button className="btn-primary" disabled={reason.trim().length < 3}>{t("Open terminal")}</button>
    </form>
  );
  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between text-xs">
        <span className={phase === "open" ? "text-success" : phase === "failed" ? "text-danger" : "text-fg-muted"}>{t(phase)}{status ? ` · ${status}` : ""}</span>
        {(phase === "closed" || phase === "failed") && <button className="btn-ghost h-7 px-2 text-xs" onClick={() => setSession(0)}>{t("New session")}</button>}
      </div>
      <div ref={box} className="h-[60vh] overflow-hidden rounded-lg border border-line bg-[#0b0e13] p-1" />
    </div>
  );
}
