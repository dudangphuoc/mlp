#!/usr/bin/env bash
# Agent build loop: the api writes {AGENTS}/queue/{ver}_{rid}.json → build → {AGENTS}/cache/{ver}/{rid}/. One build at a time, nice 19, -m:1.
set -uo pipefail
AGENTS="${MLP_AGENTS_DIR:-/agents}"
SRC=/src
# Group-writable: the api (non-root, same group) drops build requests into queue/.
umask 002
mkdir -p "$AGENTS/queue" "$AGENTS/cache"
chmod g+ws "$AGENTS/queue" "$AGENTS/cache" 2>/dev/null || true
# Version = SHA-256 of the agent source contents (code changed → new build; same code → reuse the cache).
VER=$(cd $SRC && find proto src Directory.Build.props Directory.Packages.props -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.proto' -o -name '*.props' -o -name '*.sh' -o -name 'Dockerfile' \) \
  -not -path '*/obj/*' -not -path '*/bin/*' | LC_ALL=C sort | xargs sha256sum | sha256sum | cut -c1-16)
echo "$VER" > "$AGENTS/source-version"
# Cache cleanup: keep the current version + the newest previous one (machines not yet upgraded can still re-download); delete orphaned queue entries.
ls -1t "$AGENTS/cache" 2>/dev/null | grep -vx "$VER" | tail -n +2 | while read -r old; do rm -rf "$AGENTS/cache/$old"; echo "agent-builder: removed old cache $old"; done
find "$AGENTS/queue" -type f -mmin +60 -delete 2>/dev/null || true
case "$(uname -m)" in x86_64) NATIVE=linux-x64 ;; aarch64) NATIVE=linux-arm64 ;; *) NATIVE=none ;; esac
echo "agent-builder: source $VER, native AOT for $NATIVE"
# rid "shim-linux-x64" etc. = mlp-shim for recipe images (single file, self-contained), cached next to the agents.
build() {
  local rid=$1 out="$AGENTS/cache/$VER/$1" mode exe=mlp-agent proj=$SRC/src/Hosts/MLPlatform.Agent target=$1
  [[ $rid == win-* ]] && exe=mlp-agent.exe
  if [[ $rid == shim-* ]]; then exe=mlp-shim; proj=$SRC/src/Hosts/MLPlatform.Shim; target=${rid#shim-}; fi
  mkdir -p "$out"; rm -f "$out/failed"
  if [[ $target == "$NATIVE" && $exe != mlp-shim ]]; then mode=aot; args=(-p:PublishAot=true)
  else mode=single-file; args=(-p:PublishAot=false -p:SelfContained=true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true)
  fi
  echo "[$(date -u +%FT%TZ)] build $rid ($mode)"
  local tmp; tmp=$(mktemp -d)
  if nice -n 19 dotnet publish "$proj" -c Release -r "$target" -m:1 -p:RestoreLockedMode=false "${args[@]}" \
       -p:InformationalVersion="$VER" -p:IncludeSourceRevisionInInformationalVersion=false -o "$tmp" >"$out/build.log" 2>&1 \
     && [ -f "$tmp/$exe" ]; then
    cp "$tmp/$exe" "$out/$exe.part" && mv "$out/$exe.part" "$out/$exe"
    (cd "$out" && sha256sum "$exe" > "$exe.sha256.part" && mv "$exe.sha256.part" "$exe.sha256")
    printf '{"rid":"%s","mode":"%s","version":"%s","built_at":"%s","size":%s}\n' "$rid" "$mode" "$VER" "$(date -u +%FT%TZ)" "$(stat -c %s "$out/$exe")" > "$out/build.json"
    echo "  done $rid"
  else
    touch "$out/failed"; echo "  FAILED $rid — see $out/build.log"
  fi
  rm -rf "$tmp"
}
for r in ${MLP_AGENT_PREBUILD:-}; do [ -f "$AGENTS/cache/$VER/$r/mlp-agent.sha256" ] || build "$r"; done
while true; do
  for req in "$AGENTS"/queue/*.json; do
    [ -e "$req" ] || continue
    f=$(basename "$req" .json); ver=${f%%_*}; rid=${f#*_}
    if [[ $ver == "$VER" ]] && ! ls "$AGENTS/cache/$VER/$rid"/mlp-*.sha256 >/dev/null 2>&1; then build "$rid"; fi
    rm -f "$req"
  done
  sleep 2
done
