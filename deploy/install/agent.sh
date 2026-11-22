#!/bin/sh
# Install the MLPlatform agent (Linux/macOS) — plug-and-play (20/04 §2, revised 2026-11-18). Served by the control plane at /install/agent.sh.
#   curl -fsSL https://<cp>/install/agent.sh | sudo sh -s -- --token mlpe_… --data /datasets --models /models
#   … --instance dev   # second agent on the same machine (own paths/service, yields the GPU to the default one)
#   curl -fsSL https://<cp>/install/agent.sh | sh -s -- --doctor            # check the machine only, no install
# 1) doctor: platform, Docker, GPU + NVIDIA Container Toolkit, RAM/disk, route to the control plane.
# 2) send the doctor results to the control plane → it builds the agent for this platform from its own source (cached for later machines).
# 3) download the binary (SHA-256 verified), register an OS service, the agent enrolls with the token.
# Never installs missing system software itself — only gives guidance.
set -eu
CP_URL="__MLP_PUBLIC_URL__"            # substituted by the control plane when served
AGENT_ADDR="__MLP_AGENT_ADDR__"        # host:7443
PIN="__MLP_AGENT_PIN__"                # SHA-256 of the agent channel TLS certificate (pinned — self-signed certificates work)
TOKEN=""; DATA=""; MODELS=""; DOCTOR_ONLY=0; INSTANCE=""; FS_ROOTS=""
while [ $# -gt 0 ]; do
  case "$1" in
    --token) TOKEN="$2"; shift 2 ;;
    --data) DATA="$2"; shift 2 ;;
    --models) MODELS="$2"; shift 2 ;;
    --cp) CP_URL="$2"; shift 2 ;;
    --doctor) DOCTOR_ONLY=1; shift ;;
    --instance) INSTANCE="$2"; shift 2 ;;
    --fs-root) FS_ROOTS="$FS_ROOTS $2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

step() { printf '\n==> %s\n' "$1"; }
OKS=0; WARNS=0; FAILS=0
ok()   { printf '  \033[32m✔\033[0m %-26s %s\n' "$1" "$2"; OKS=$((OKS+1)); }
warn() { printf '  \033[33m⚠\033[0m %-26s %s\n' "$1" "$2"; WARNS=$((WARNS+1)); }
bad()  { printf '  \033[31m✖\033[0m %-26s %s\n' "$1" "$2"; FAILS=$((FAILS+1)); }
json_str() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g' | tr -d '\n\r'; }

# ───────── doctor ─────────
step "Doctor — checking this machine"
OS=$(uname -s); ARCH=$(uname -m); KERNEL=$(uname -r); LIBC=""; [ "$OS" = Linux ] && LIBC=glibc
if [ "$OS" = Linux ] && ldd --version 2>&1 | grep -qi musl; then LIBC=musl; fi
case "$OS/$ARCH" in
  Linux/x86_64|Linux/aarch64|Linux/arm64|Darwin/arm64|Darwin/x86_64) ok "Platform" "$OS/$ARCH ($LIBC, kernel $KERNEL)" ;;
  *) bad "Platform" "$OS/$ARCH is not supported" ;;
esac
if [ "$OS" = Linux ]; then
  CPUS=$(nproc 2>/dev/null || echo 0); MEM_KB=$(awk '/MemTotal/ {print $2}' /proc/meminfo 2>/dev/null || echo 0); MEM=$((MEM_KB*1024))
else
  CPUS=$(sysctl -n hw.ncpu 2>/dev/null || echo 0); MEM=$(sysctl -n hw.memsize 2>/dev/null || echo 0)
fi
ok "CPU / RAM" "$CPUS cores, $((MEM/1073741824)) GiB"
[ "$((MEM/1073741824))" -lt 4 ] && warn "RAM" "under 4 GiB — suitable for small jobs only"

DOCKER_VER=""; DOCKER_RUNTIMES=""
if ! command -v docker >/dev/null 2>&1; then
  if [ "$OS" = Darwin ]; then bad "Docker" "not installed — Docker Desktop: https://docs.docker.com/desktop/setup/install/mac-install/"
  else bad "Docker" "not installed — https://docs.docker.com/engine/install/"; fi
elif ! docker info >/dev/null 2>&1; then
  bad "Docker" "installed but the daemon is not responding (not running, or missing permissions — run with sudo)"
else
  DOCKER_VER=$(docker version --format '{{.Server.Version}}' 2>/dev/null || echo "?")
  DOCKER_RUNTIMES=$(docker info --format '{{range $k, $v := .Runtimes}}{{$k}} {{end}}' 2>/dev/null || echo "")
  case "$DOCKER_VER" in 1*|2[0-3].*) warn "Docker" "$DOCKER_VER — 24 or later recommended" ;; *) ok "Docker" "$DOCKER_VER" ;; esac
fi

GPU_COUNT=0; GPU_NAMES=""; GPU_DRIVER=""; CUDA=""
if command -v nvidia-smi >/dev/null 2>&1 && nvidia-smi -L >/dev/null 2>&1; then
  GPU_COUNT=$(nvidia-smi -L | grep -c '^GPU')
  GPU_NAMES=$(nvidia-smi --query-gpu=name,memory.total --format=csv,noheader | paste -sd ';' -)
  GPU_DRIVER=$(nvidia-smi --query-gpu=driver_version --format=csv,noheader | head -1)
  CUDA=$(nvidia-smi 2>/dev/null | sed -n 's/.*CUDA Version: *\([0-9.]*\).*/\1/p' | head -1)
  ok "GPU NVIDIA" "$GPU_COUNT x $GPU_NAMES (driver $GPU_DRIVER, CUDA $CUDA)"
  if echo "$DOCKER_RUNTIMES" | grep -q nvidia || [ -e /usr/bin/nvidia-container-toolkit ] || command -v nvidia-ctk >/dev/null 2>&1; then
    ok "NVIDIA Container Toolkit" "installed"
  else
    bad "NVIDIA Container Toolkit" "missing — https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html"
  fi
else
  ok "GPU" "no NVIDIA GPU — this machine runs CPU jobs"
fi

for p in "$DATA" "$MODELS"; do
  [ -z "$p" ] && continue
  d="$p"; while [ ! -d "$d" ] && [ "$d" != / ]; do d=$(dirname "$d"); done
  FREE=$(df -Pk "$d" | awk 'NR==2 {print $4}')
  if [ "$((FREE/1048576))" -lt 20 ]; then warn "Disk $p" "$((FREE/1048576)) GiB free (< 20 GiB)"; else ok "Disk $p" "$((FREE/1048576)) GiB free"; fi
done

if curl -fsS -m 5 "$CP_URL/health/live" >/dev/null 2>&1; then ok "Control plane" "$CP_URL"; else bad "Control plane" "cannot reach $CP_URL"; fi
CP_HOST=${AGENT_ADDR%:*}; CP_PORT=${AGENT_ADDR##*:}
if command -v nc >/dev/null 2>&1; then
  if nc -z -w 5 "$CP_HOST" "$CP_PORT" >/dev/null 2>&1; then ok "Agent channel" "$AGENT_ADDR"; else bad "Agent channel" "cannot open TCP $AGENT_ADDR (firewall?)"; fi
else warn "Agent channel" "nc not available — skipping the $AGENT_ADDR port check"; fi

printf '\n  Doctor: %s passed, %s warnings, %s errors\n' "$OKS" "$WARNS" "$FAILS"
DOCTOR=$(printf '{"os":"%s","arch":"%s","libc":"%s","kernel":"%s","cpus":%s,"memory_bytes":%s,"docker":"%s","docker_runtimes":"%s","gpu_count":%s,"gpus":"%s","gpu_driver":"%s","cuda":"%s","warnings":%s,"errors":%s}' \
  "$OS" "$ARCH" "$LIBC" "$(json_str "$KERNEL")" "${CPUS:-0}" "${MEM:-0}" "$(json_str "$DOCKER_VER")" "$(json_str "$DOCKER_RUNTIMES")" \
  "$GPU_COUNT" "$(json_str "$GPU_NAMES")" "$(json_str "$GPU_DRIVER")" "$(json_str "$CUDA")" "$WARNS" "$FAILS")
[ "$DOCTOR_ONLY" = 1 ] && { echo "$DOCTOR"; [ "$FAILS" = 0 ]; exit $?; }
[ "$FAILS" = 0 ] || { echo "Fix the ✖ items, then run the install command again." >&2; exit 3; }
[ -n "$TOKEN" ] && [ -n "$DATA" ] && [ -n "$MODELS" ] || { echo "--token, --data and --models are required" >&2; exit 2; }
[ "$(id -u)" = 0 ] || { echo "Run with sudo/root" >&2; exit 2; }

# ───────── per-machine build ─────────
step "Asking the control plane to build the agent for this machine"
RESP=$(curl -fsS -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" --data "$DOCTOR" "$CP_URL/install/agent/build") \
  || { echo "The control plane refused (token expired/already used, or platform not supported)" >&2; exit 4; }
field() { printf '%s' "$RESP" | sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p"; }
RID=$(field rid); WAIT=0
while [ "$(field status)" = building ]; do
  [ "$WAIT" -ge 1200 ] && { echo "Build took longer than 20 minutes — try again later" >&2; exit 4; }
  [ $((WAIT % 30)) = 0 ] && echo "  building agent $RID (the first build for this platform takes a few minutes)…"
  sleep 5; WAIT=$((WAIT+5))
  RESP=$(curl -fsS "$CP_URL/install/agent/build/$RID")
done
[ "$(field status)" = ready ] || { echo "Agent build $RID failed:"; printf '%s\n' "$RESP" >&2; exit 4; }
URL=$(field url); EXPECTED=$(field sha256)
echo "  agent $RID ready (source version $(field version))"

case "$INSTANCE" in ""|[a-z0-9]*) ;; *) echo "--instance must be lowercase letters/digits" >&2; exit 2 ;; esac
SFX=""; [ -n "$INSTANCE" ] && SFX="-$INSTANCE"
UNIT="mlplatform-agent$SFX"; LABEL="io.mlplatform.agent"; [ -n "$INSTANCE" ] && LABEL="$LABEL.$INSTANCE"
if [ "$OS" = Darwin ]; then
  PREFIX=/usr/local/mlplatform-agent$SFX; STATE="/Library/Application Support/MLPlatform$SFX"
else
  PREFIX=/opt/mlplatform-agent$SFX; STATE=/var/lib/mlplatform-agent$SFX
fi
mkdir -p "$PREFIX" "$STATE" "$DATA" "$MODELS"

step "Downloading agent ($RID)"
TMP=$(mktemp -d)
curl -fsSL "$CP_URL$URL" -o "$TMP/mlp-agent"
if command -v sha256sum >/dev/null 2>&1; then ACTUAL=$(sha256sum "$TMP/mlp-agent" | cut -d' ' -f1); else ACTUAL=$(shasum -a 256 "$TMP/mlp-agent" | cut -d' ' -f1); fi
[ "$EXPECTED" = "$ACTUAL" ] || { echo "SHA-256 mismatch — aborting" >&2; exit 4; }
install -m 0755 "$TMP/mlp-agent" "$PREFIX/mlp-agent"
rm -rf "$TMP"

step "Registering the service"
ARGS="--state \"$STATE/agent.json\" --cp $AGENT_ADDR --token $TOKEN --data \"$DATA\" --models \"$MODELS\""
[ -n "$PIN" ] && ARGS="$ARGS --pin $PIN"
EXTRA_XML=""
if [ -n "$INSTANCE" ]; then ARGS="$ARGS --instance $INSTANCE"; EXTRA_XML="<string>--instance</string><string>$INSTANCE</string>"; fi
for r in $FS_ROOTS; do ARGS="$ARGS --fs-root \"$r\""; EXTRA_XML="$EXTRA_XML<string>--fs-root</string><string>$r</string>"; done
if [ "$OS" = Darwin ]; then ARGS="$ARGS --service launchd"; SVC_XML="<string>--service</string><string>launchd</string>"; else ARGS="$ARGS --service systemd"; SVC_XML=""; fi
PIN_XML=""; [ -n "$PIN" ] && PIN_XML="<string>--pin</string><string>$PIN</string>"
if [ "$OS" = Darwin ]; then
  PLIST=/Library/LaunchDaemons/$LABEL.plist
  cat > "$PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key><array>
    <string>$PREFIX/mlp-agent</string><string>--state</string><string>$STATE/agent.json</string>
    <string>--cp</string><string>$AGENT_ADDR</string><string>--token</string><string>$TOKEN</string>
    <string>--data</string><string>$DATA</string><string>--models</string><string>$MODELS</string>$PIN_XML$SVC_XML$EXTRA_XML
  </array>
  <key>RunAtLoad</key><true/><key>KeepAlive</key><true/>
  <key>StandardErrorPath</key><string>$STATE/agent.log</string><key>StandardOutPath</key><string>$STATE/agent.log</string>
</dict></plist>
PLIST
  launchctl bootout system/$LABEL 2>/dev/null || true
  launchctl bootstrap system "$PLIST"
else
  cat > /etc/systemd/system/$UNIT.service <<UNIT
[Unit]
Description=MLPlatform agent $INSTANCE
After=network-online.target docker.service
Wants=network-online.target

[Service]
ExecStart=$PREFIX/mlp-agent $ARGS
Restart=always
RestartSec=5
SuccessExitStatus=75
RestartForceExitStatus=75

[Install]
WantedBy=multi-user.target
UNIT
  systemctl daemon-reload
  systemctl enable --now "$UNIT"
fi
step "Done — the machine will appear on the Machines (Fleet) page once it connects"
