import json
import os
import secrets
import socket
import subprocess
import sys
import threading
import time

REMOTE_ROOT = "/srv/code"
IMAGE = "python:3.12-slim"
DEBUGPY_LIBS = os.environ.get("MLP_PROTO_DEBUGPY", "")


def init():
    if os.environ.get("MLP_RUN_ID"):
        return
    machine = os.environ.get("MLP_MACHINE", "local")
    if machine == "local":
        print("mlp: local", flush=True)
        return
    root = os.path.dirname(os.path.abspath(sys.argv[0]))
    target = _debug_target()
    name = "mlp-g0-" + secrets.token_hex(4)
    cmd = ["docker", "run", "--rm", "--name", name,
           "-v", f"{root}:{REMOTE_ROOT}:ro",
           "-v", f"{DEBUGPY_LIBS}:/opt/dbg:ro",
           "-e", "PYTHONPATH=/opt/dbg", "-e", "MLP_RUN_ID=g0", "-e", "PYTHONUNBUFFERED=1",
           "-w", REMOTE_ROOT]
    if target:
        port = _start_relay(target[0], target[1])
        cmd += ["-e", "PATHS_FROM_ECLIPSE_TO_PYTHON=" + json.dumps([[root, REMOTE_ROOT]])]
        cmd += [IMAGE, "python", "-Xfrozen_modules=off", "-m", "debugpy", "--connect", f"host.docker.internal:{port}",
                "--adapter-access-token", target[2], "--parent-session-pid", str(os.getpid())]
    else:
        cmd += [IMAGE, "python"]
    cmd += [REMOTE_ROOT + "/" + os.path.basename(sys.argv[0])] + sys.argv[1:]
    print(f"mlp: remote on {machine} debug={'on' if target else 'off'} container={name}", flush=True)
    started = time.time()
    replicas = int(os.environ.get("MLP_PROTO_REPLICAS", "1"))
    procs = []
    for i in range(replicas):
        c = list(cmd)
        c[c.index(name)] = f"{name}-{i}"
        procs.append(subprocess.Popen(c, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True))
    for p in procs[1:]:
        _daemon(lambda p=p: [sys.stdout.write(l) for l in p.stdout])
    proc = procs[0]
    try:
        for line in proc.stdout:
            sys.stdout.write(line)
            sys.stdout.flush()
        code = proc.wait()
        for p in procs[1:]:
            p.wait()
    except KeyboardInterrupt:
        subprocess.run(["docker", "kill", name], capture_output=True)
        code = 130
    print(f"mlp: remote exit {code} after {time.time() - started:.1f}s", flush=True)
    sys.stdout.flush()
    os._exit(code)


def _debug_target():
    try:
        import debugpy
        import pydevd
    except ImportError:
        return None
    setup = getattr(pydevd.SetupHolder, "setup", None)
    if not setup or not debugpy.is_client_connected():
        return None
    return setup["client"], int(setup["port"]), setup.get("client-access-token")


def _start_relay(host, port):
    server = socket.socket()
    server.bind(("127.0.0.1", 0))
    server.listen(4)

    def accept():
        while True:
            inbound, _ = server.accept()
            outbound = socket.create_connection((host, port))
            _daemon(lambda a=inbound, b=outbound: _pump(a, b, True))
            _daemon(lambda a=outbound, b=inbound: _pump(a, b, False))

    _daemon(accept)
    return server.getsockname()[1]


_pid_counter = [0]
_pid_map = {}


def _pump(src, dst, from_remote):
    delay = int(os.environ.get("MLP_PROTO_DELAY_MS", "0")) / 1000
    rewrite = from_remote and os.environ.get("MLP_PROTO_REWRITE_PID") == "1"
    buf = b""
    try:
        while data := src.recv(65536):
            if delay:
                time.sleep(delay)
            if rewrite:
                buf += data
                data, buf = _rewrite_frames(buf)
            dst.sendall(data)
    except OSError:
        pass
    finally:
        for s in (src, dst):
            try:
                s.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass


def _daemon(fn):
    t = threading.Thread(target=fn, daemon=True)
    t.is_pydev_daemon_thread = True
    t.start()


def _rewrite_frames(buf):
    out = b""
    while True:
        head_end = buf.find(b"\r\n\r\n")
        if head_end < 0:
            return out, buf
        length = int(buf[:head_end].split(b":")[1])
        start = head_end + 4
        if len(buf) < start + length:
            return out, buf
        body = buf[start:start + length]
        buf = buf[start + length:]
        msg = json.loads(body)
        if msg.get("type") == "response" and msg.get("command") == "pydevdSystemInfo":
            _pid_counter[0] += 1
            proc = msg["body"]["process"]
            fake = 900000 + _pid_counter[0]
            _pid_map[proc["pid"]] = fake
            if proc.get("ppid") in _pid_map:
                proc["ppid"] = _pid_map[proc["ppid"]]
            proc["pid"] = fake
            body = json.dumps(msg).encode()
        out += b"Content-Length: %d\r\n\r\n" % len(body) + body
