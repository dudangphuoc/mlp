#!/usr/bin/env bash
# Publish MLPlatform binaries into artifacts/publish/<target>/<rid>/.
#
#   deploy/publish.sh api                      # Native AOT for this machine (e.g. osx-arm64)
#   deploy/publish.sh api --rid linux-arm64    # Linux api: built inside Docker (Native AOT cannot cross OS)
#   deploy/publish.sh agent --rid win-x64      # agent/shim/cli for another OS: self-contained single file
#   deploy/publish.sh all
#
# Rules: Native AOT only when the target RID is this machine's RID (host csproj files already set PublishAot;
# passing -p:PublishAot=true globally would also hit the netstandard generator projects → NETSDK1207).
# Never PublishSingleFile/ReadyToRun with AOT and no RID: that is the NETSDK1097 failure of the IDE publish dialog.
# A RID publish adds "<tfm>/<rid>" sections to packages.lock.json; the script puts the committed lock files back.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
HOST_RID="$(dotnet --info | awk '/ RID:/ {print $2; exit}')"
TARGET="${1:-}"; shift || true
RID="$HOST_RID"; OUT_BASE="$ROOT/artifacts/publish"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid) RID="$2"; shift 2 ;;
    --out) OUT_BASE="$2"; shift 2 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

usage() { sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
[[ -z "$TARGET" ]] && usage

project_of() {
  case "$1" in
    api)   echo "src/Hosts/MLPlatform.Api" ;;
    agent) echo "src/Hosts/MLPlatform.Agent" ;;
    shim)  echo "src/Hosts/MLPlatform.Shim" ;;
    cli)   echo "tools/MLPlatform.Cli" ;;
    *) echo "unknown target: $1 (api|agent|shim|cli|all)" >&2; exit 2 ;;
  esac
}

exe_of() {
  local name
  case "$1" in api) name=mlp-api ;; agent) name=mlp-agent ;; shim) name=mlp-shim ;; cli) name=mlp ;; esac
  [[ "$2" == win-* ]] && name="$name.exe"
  echo "$name"
}

load_ok() {
  local load; load=$(ps -A -o %cpu= | awk '{s+=$1} END {print int(s)}')
  if (( load > 400 )); then echo "CPU load is ${load}% — wait for it to drop before publishing." >&2; exit 1; fi
}

report() {
  local dir=$1 exe=$2
  [[ -f "$dir/$exe" ]] || { echo "  missing $dir/$exe" >&2; exit 1; }
  local size; size=$(du -h "$dir/$exe" | cut -f1)
  (cd "$dir" && shasum -a 256 "$exe" > "$exe.sha256")
  echo "  $dir/$exe ($size)"
}

publish_dotnet() {
  local target=$1 rid=$2 out=$3 mode args
  local proj; proj=$(project_of "$target")
  if [[ "$rid" == "$HOST_RID" ]]; then
    mode="native AOT"; args=(-p:PublishSingleFile=false)
  else
    mode="self-contained single file"
    args=(-p:PublishAot=false -p:SelfContained=true -p:PublishSingleFile=true
          -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true)
  fi
  echo "publish $target → $rid ($mode)"
  rm -rf "$out"; mkdir -p "$out"
  (cd "$ROOT" && MSBUILDDISABLENODEREUSE=1 nice -n 19 dotnet publish "$proj" -c Release -r "$rid" -m:1 \
     -p:PublishReadyToRun=false "${args[@]}" -o "$out")
}

copy_api_assets() {
  local out=$1
  mkdir -p "$out/install"
  cp "$ROOT/deploy/install/agent.sh" "$ROOT/deploy/install/agent.ps1" "$out/install/"
  rm -rf "$out/db" "$out/recipes"
  cp -R "$ROOT/db" "$out/db"
  cp -R "$ROOT/recipes" "$out/recipes"
}

publish_api_in_docker() {
  local rid=$1 out=$2 platform
  case "$rid" in
    linux-arm64) platform=linux/arm64 ;;
    linux-x64)   platform=linux/amd64 ;;
    *) echo "api can only be published for $HOST_RID (local) or linux-arm64/linux-x64 (Docker)" >&2; exit 2 ;;
  esac
  local tag="mlp/api:publish-${rid}"
  echo "publish api → $rid (native AOT inside Docker, $platform)"
  [[ "$platform" != "linux/$(docker version --format '{{.Server.Arch}}')" ]] && echo "  note: $platform is emulated on this machine — expect a slow build"
  (cd "$ROOT" && DOCKER_BUILDKIT=1 nice -n 19 docker build --platform "$platform" -f deploy/docker/api.Dockerfile -t "$tag" .)
  rm -rf "$out"; mkdir -p "$out"
  local cid; cid=$(docker create --platform "$platform" "$tag")
  docker cp "$cid:/app/." "$out/" >/dev/null
  docker rm "$cid" >/dev/null
  echo "  image: $tag"
}

publish_one() {
  local target=$1 out="$OUT_BASE/$1/$RID"
  local exe; exe=$(exe_of "$target" "$RID")
  load_ok
  if [[ "$target" == api && "$RID" != "$HOST_RID" ]]; then
    publish_api_in_docker "$RID" "$out"
  else
    publish_dotnet "$target" "$RID" "$out"
    [[ "$target" == api ]] && copy_api_assets "$out"
  fi
  report "$out" "$exe"
}

LOCK_SNAPSHOT="$(mktemp -d)"
snapshot_lock_files() {
  (cd "$ROOT" && git ls-files '*packages.lock.json' | while read -r f; do
     mkdir -p "$LOCK_SNAPSHOT/$(dirname "$f")"; cp "$f" "$LOCK_SNAPSHOT/$f"; done)
}
restore_lock_files() {
  (cd "$LOCK_SNAPSHOT" && find . -name packages.lock.json | while read -r f; do
     cmp -s "$f" "$ROOT/$f" || cp "$f" "$ROOT/$f"; done)
  rm -rf "$LOCK_SNAPSHOT"
}
cleanup() {
  restore_lock_files
  dotnet build-server shutdown >/dev/null 2>&1 || true
}
snapshot_lock_files
trap cleanup EXIT
if [[ "$TARGET" == all ]]; then
  for t in api agent shim cli; do publish_one "$t"; done
else
  project_of "$TARGET" >/dev/null
  publish_one "$TARGET"
fi
