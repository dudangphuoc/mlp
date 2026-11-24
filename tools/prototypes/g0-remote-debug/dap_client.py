import json
import os
import queue
import socket
import subprocess
import sys
import threading
import time

LIBS = os.environ["MLP_PROTO_DEBUGPY"]
PROJ = os.path.join(os.path.dirname(os.path.abspath(__file__)), "proj")
TRAIN = os.path.join(PROJ, os.environ.get("MLP_PROTO_SCRIPT", "train.py"))
HELPER = os.path.join(PROJ, "helper.py")
BREAKPOINTS = {TRAIN: [11]} if TRAIN.endswith("train.py") else {}
BREAKPOINTS[HELPER] = [2]
T0 = time.time()


def log(msg):
    print(f"[{time.time() - T0:6.2f}s] {msg}", flush=True)


class Session:
    def __init__(self, name, rfile, wfile, on_reverse=None):
        self.name, self.r, self.w = name, rfile, wfile
        self.seq = 0
        self.responses, self.events = {}, queue.Queue()
        self.cond = threading.Condition()
        self.on_reverse = on_reverse
        self.output = []
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        while True:
            header = b""
            while not header.endswith(b"\r\n\r\n"):
                ch = self.r.read(1)
                if not ch:
                    self.events.put({"event": "__closed__"})
                    return
                header += ch
            length = int([l for l in header.decode().split("\r\n") if l.lower().startswith("content-length")][0].split(":")[1])
            msg = json.loads(self.r.read(length))
            if msg["type"] == "response":
                with self.cond:
                    self.responses[msg["request_seq"]] = msg
                    self.cond.notify_all()
            elif msg["type"] == "event":
                if msg["event"] == "output":
                    self.output.append(msg["body"].get("output", ""))
                else:
                    self.events.put(msg)
            elif msg["type"] == "request":
                self.send({"type": "response", "request_seq": msg["seq"], "success": True, "command": msg["command"]})
                if self.on_reverse:
                    threading.Thread(target=self.on_reverse, args=(msg,), daemon=True).start()

    def send(self, msg):
        with self.cond:
            self.seq += 1
            msg["seq"] = self.seq
            data = json.dumps(msg).encode()
            self.w.write(f"Content-Length: {len(data)}\r\n\r\n".encode() + data)
            self.w.flush()
            return self.seq

    def request(self, command, arguments=None, wait=True, timeout=60):
        seq = self.send({"type": "request", "command": command, "arguments": arguments or {}})
        return self.response(seq, timeout) if wait else seq

    def response(self, seq, timeout=60):
        with self.cond:
            if not self.cond.wait_for(lambda: seq in self.responses, timeout):
                raise TimeoutError(f"{self.name}: no response to {seq}")
            resp = self.responses[seq]
        if not resp["success"]:
            raise RuntimeError(f"{self.name}: {resp.get('command')} failed: {resp.get('message')}")
        return resp

    def wait_event(self, name, timeout=60):
        deadline = time.time() + timeout
        while True:
            ev = self.events.get(timeout=max(0.01, deadline - time.time()))
            if ev["event"] == name or (isinstance(name, tuple) and ev["event"] in name):
                return ev
            if ev["event"] == "__closed__":
                raise RuntimeError(f"{self.name}: closed while waiting for {name}")

    def configure(self):
        self.wait_event("initialized")
        for path, lines in BREAKPOINTS.items():
            resp = self.request("setBreakpoints", {"source": {"path": path}, "breakpoints": [{"line": l} for l in lines]})
            log(f"{self.name}: breakpoints {os.path.basename(path)} verified={[b['verified'] for b in resp['body']['breakpoints']]}")
        self.request("configurationDone")


INIT = {"clientID": "vscode", "adapterID": "debugpy", "pathFormat": "path", "linesStartAt1": True,
        "columnsStartAt1": True, "supportsVariableType": True, "supportsRunInTerminalRequest": False,
        "supportsStartDebuggingRequest": True}

children = queue.Queue()


def on_reverse(msg):
    if msg["command"] != "startDebugging":
        return
    cfg = msg["arguments"]["configuration"]
    log(f"startDebugging received: {cfg['name']} connect={cfg['connect']}")
    sock = socket.create_connection((cfg["connect"]["host"], cfg["connect"]["port"]))
    child = Session(cfg["name"], sock.makefile("rb"), sock.makefile("wb"), on_reverse)
    child.request("initialize", INIT)
    attach = child.request("attach", cfg, wait=False)
    child.configure()
    child.response(attach)
    child.ready_at = time.time()
    children.put(child)


def main(mode):
    env = dict(os.environ, PYTHONPATH=LIBS)
    adapter = subprocess.Popen([sys.executable, "-m", "debugpy.adapter", "--log-dir", os.environ.get("MLP_PROTO_LOGS", "")] if os.environ.get("MLP_PROTO_LOGS") else [sys.executable, "-m", "debugpy.adapter"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, env=env)
    parent = Session("parent", adapter.stdout, adapter.stdin, on_reverse)
    parent.request("initialize", INIT)
    launch = {"type": "python", "request": "launch", "name": "Python: Current File", "program": TRAIN, "cwd": PROJ,
              "console": "internalConsole", "justMyCode": True,
              "env": {"MLP_MACHINE": "gpu-sim", "MLP_PROTO_DEBUGPY": LIBS}}
    if mode == "nodebug":
        launch["noDebug"] = True
    pending = parent.request("launch", launch, wait=False)
    if mode != "nodebug":
        parent.configure()
    parent.response(pending)
    log("parent launched (F5)")
    if mode == "nodebug":
        ev = parent.wait_event("exited", 120)
        log(f"parent exited code={ev['body']['exitCode']}")
        print("".join(parent.output))
        return
    child = children.get(timeout=120)
    log(f"child session ready: {child.name}")
    if mode == "mp":
        stops = []
        def drain(sess):
            while True:
                try:
                    ev = sess.wait_event(("stopped", "terminated"), 20)
                except Exception:
                    return
                if ev["event"] != "stopped":
                    return
                tid = ev["body"]["threadId"]
                top = sess.request("stackTrace", {"threadId": tid})["body"]["stackFrames"][0]
                stops.append(f"{sess.name}@{os.path.basename(top['source']['path'])}:{top['line']}")
                sess.request("continue", {"threadId": tid})
        threading.Thread(target=drain, args=(child,), daemon=True).start()
        deadline = time.time() + 15
        names = [child.name]
        while time.time() < deadline:
            try:
                c = children.get(timeout=1)
            except queue.Empty:
                continue
            names.append(c.name)
            threading.Thread(target=drain, args=(c,), daemon=True).start()
        log(f"sessions: {names}")
        log(f"breakpoint stops: {stops}")
        names = [child.name]
        while not children.empty():
            names.append(children.get().name)
        log(f"sessions: {names}")
        ev = parent.wait_event("exited", 60)
        log(f"parent exited code={ev['body']['exitCode']}")
        print("".join(parent.output)[-600:])
        return
    if mode == "replicas":
        try:
            second = children.get(timeout=15)
            log(f"second child session ready: {second.name}")
        except queue.Empty:
            log("second child session NEVER arrived")
    stops = 0
    while True:
        try:
            ev = child.wait_event(("stopped", "terminated", "exited"), 60)
        except RuntimeError:
            break
        if ev["event"] != "stopped":
            log(f"child {ev['event']} {ev.get('body', {})}")
            break
        stops += 1
        stopped_at = time.time()
        tid = ev["body"]["threadId"]
        t = time.time()
        frames = child.request("stackTrace", {"threadId": tid})["body"]["stackFrames"]
        rtt = time.time() - t
        top = frames[0]
        value = child.request("evaluate", {"expression": "total" if top["source"]["path"] == TRAIN else "value", "frameId": top["id"], "context": "watch"})["body"]["result"]
        log(f"STOP #{stops} {ev['body']['reason']} at {top['source']['path']}:{top['line']} value={value} stackTrace_rtt={rtt*1000:.0f}ms")
        if stops == 1:
            log(f"first stop {stopped_at - child.ready_at:.2f}s after child configurationDone")
            t = time.time()
            child.request("next", {"threadId": tid})
            ev2 = child.wait_event("stopped", 30)
            log(f"step 'next' round trip {(time.time()-t)*1000:.0f}ms -> {ev2['body']['reason']}")
            if mode == "terminate":
                log("ending the debug session from the IDE (Stop button)")
                parent.request("disconnect", {"terminateDebuggee": True}, timeout=30)
                time.sleep(8)
                left = subprocess.run(["docker", "ps", "--filter", "name=mlp-g0-", "--format", "{{.Names}} {{.Status}}"], capture_output=True, text=True).stdout.strip()
                log(f"containers still running after Stop: {left or 'none'}")
                return
            tid = ev2["body"]["threadId"]
        child.request("continue", {"threadId": tid})
    ev = parent.wait_event("exited", 60)
    log(f"total stops={stops}; parent exited code={ev['body']['exitCode']}")
    print("--- parent output (IDE debug console) ---")
    print("".join(parent.output))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "full")
