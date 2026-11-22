#!/bin/sh
# MLPlatform agent OTA (D-38) — the control plane generates this from the template; the agent runs it DETACHED from itself (systemd-run / launchctl submit).
# Steps are written to status.txt (the control plane reads it via FileRead); the control plane writes final-ok once the new version has been connected stably for ≥ 30 s.
# No final-ok within 240 s → restore the old version (kept). Machine rebooted midway → the recover job runs `upgrade.sh recover`.
# Containers (Ray node, workloads) are NOT stopped: the agent is only a bridge, running work survives the agent swap.
set -u
JOB="$(cd "$(dirname "$0")" && pwd)"
STATUS="$JOB/status.txt"; PHASE="$JOB/phase"
EXE='__EXE__'; URL='__URL__'; SHA='__SHA__'; SVC='__SVC__'; INST='__INST__'
SFX=''; LBL=io.mlplatform.agent; [ -n "$INST" ] && SFX="-$INST" && LBL="$LBL.$INST"
say() { echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) $*" >> "$STATUS"; }
phase() { echo "$1" > "$PHASE.tmp" && mv -f "$PHASE.tmp" "$PHASE"; say "$1"; }
sha() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }
svc_stop() {
  case "$SVC" in
    systemd) systemctl stop "mlplatform-agent$SFX" ;;
    launchd) launchctl bootout system/$LBL 2>/dev/null || true ;;
  esac
}
svc_start() {
  case "$SVC" in
    systemd) systemctl start "mlplatform-agent$SFX" ;;
    launchd) launchctl bootstrap system "/Library/LaunchDaemons/$LBL.plist" 2>/dev/null || launchctl kickstart -k "system/$LBL" ;;
  esac
}
recovery_on() {
  case "$SVC" in
    systemd) cat > /etc/systemd/system/mlplatform-agent$SFX-upgrade-recover.service <<UNIT
[Unit]
Description=MLPlatform agent upgrade recovery
After=network-online.target
[Service]
Type=oneshot
ExecStart=/bin/sh $JOB/upgrade.sh recover
[Install]
WantedBy=multi-user.target
UNIT
      systemctl daemon-reload; systemctl enable mlplatform-agent$SFX-upgrade-recover.service >/dev/null 2>&1 ;;
    launchd) cat > /Library/LaunchDaemons/$LBL-upgrade-recover.plist <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>Label</key><string>$LBL-upgrade-recover</string>
<key>ProgramArguments</key><array><string>/bin/sh</string><string>$JOB/upgrade.sh</string><string>recover</string></array>
<key>RunAtLoad</key><true/></dict></plist>
PLIST
      ;;
  esac
}
recovery_off() {
  case "$SVC" in
    systemd) systemctl disable mlplatform-agent$SFX-upgrade-recover.service >/dev/null 2>&1; rm -f /etc/systemd/system/mlplatform-agent$SFX-upgrade-recover.service; systemctl daemon-reload ;;
    launchd) rm -f /Library/LaunchDaemons/$LBL-upgrade-recover.plist ;;
  esac
}
put() { cp "$1" "$EXE.new" && chmod 0755 "$EXE.new" && mv -f "$EXE.new" "$EXE"
  [ "$(uname -s)" = Darwin ] && { xattr -d com.apple.quarantine "$EXE" 2>/dev/null; codesign --force --sign - "$EXE" 2>/dev/null; }; return 0; }
rollback() {
  phase "rolling back"
  svc_stop; put "$JOB/agent.kept"; svc_start; recovery_off
  say "rolled back: $1"; exit 1
}
wait_ok() {   # wait for the control plane to confirm the new version
  i=0; while [ $i -lt 240 ]; do [ -f "$JOB/final-ok" ] && return 0; sleep 2; i=$((i+2)); done; return 1
}

# One upgrade at a time per machine
LOCK=/tmp/mlplatform-agent$SFX-upgrade.lock
if ! mkdir "$LOCK" 2>/dev/null; then
  if [ -f "$LOCK/pid" ] && kill -0 "$(cat "$LOCK/pid")" 2>/dev/null; then say "failed: another upgrade is already running"; exit 1; fi
  rm -rf "$LOCK"; mkdir "$LOCK"
fi
echo $$ > "$LOCK/pid"; trap 'rm -rf "$LOCK"' EXIT

if [ "${1:-run}" = recover ]; then
  p=$(cat "$PHASE" 2>/dev/null || echo "")
  case "$p" in
    stopping|installing|starting|connecting)
      if [ -f "$JOB/final-ok" ]; then phase done; recovery_off; exit 0; fi
      say "recovering after restart (was at $p)"; svc_start; wait_ok && { phase done; recovery_off; exit 0; }
      rollback "machine restarted during the upgrade and the new version did not connect" ;;
    *) recovery_off; exit 0 ;;
  esac
fi

[ "$SVC" = systemd ] || [ "$SVC" = launchd ] || { say "failed: the agent is not running under a service manager ($SVC) — reinstall with the install command"; exit 1; }
FREE=$(df -Pk "$JOB" | awk 'NR==2 {print $4}'); [ "${FREE:-0}" -gt 307200 ] || { say "failed: at least 300 MB of free space is required"; exit 1; }
phase downloading
curl -fsSL --retry 3 "$URL" -o "$JOB/agent.new" || { say "failed: could not download the new version"; exit 1; }
phase checking
[ "$(sha "$JOB/agent.new")" = "$SHA" ] || { say "failed: SHA-256 mismatch"; exit 1; }
cp "$EXE" "$JOB/agent.kept" || { say "failed: could not back up the old version"; exit 1; }
recovery_on
phase stopping; svc_stop
phase installing; put "$JOB/agent.new" || rollback "could not replace the file"
phase starting; svc_start
phase connecting
wait_ok || rollback "the new version did not connect to the control plane within 240 s"
recovery_off; rm -f "$JOB/agent.new" "$JOB/agent.kept"
phase done
