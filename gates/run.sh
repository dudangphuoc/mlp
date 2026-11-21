#!/usr/bin/env bash
# CI gates (P0-11) — run sequentially with little CPU (CLAUDE.md). Red only on NEW violations.
#   gates/run.sh          — proto lint+breaking, SqlGate/CommentGate/migration, build -warnaserror, tsc web
#   gates/run.sh --aot    — also run the AOT gate: publish Native AOT api/agent/shim; trim/AOT warnings are errors (~5–10 min on one core)
#   gates/run.sh --accept-proto  — accept the current proto contract as the new breaking-change baseline (after review)
set -euo pipefail
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; cd "$ROOT"
BUF="${BUF:-$(command -v buf || echo ~/go/bin/buf)}"
fail() { printf "FAIL %s\n" "$1"; exit 1; }
ok() { printf 'OK   %s\n' "$1"; }
if [[ " $* " == *" --accept-proto "* ]]; then (cd proto && "$BUF" build -o ../gates/proto-baseline.binpb); ok "new proto baseline"; exit 0; fi

if (cd proto && "$BUF" lint); then ok "buf lint"; else fail "buf lint"; fi
if (cd proto && "$BUF" breaking --against ../gates/proto-baseline.binpb); then ok "buf breaking (against gates/proto-baseline.binpb)"; else fail "buf breaking (against gates/proto-baseline.binpb)"; fi
nice -n 19 dotnet build tools/MLPlatform.SqlGate -m:1 -v q >/dev/null
if dotnet tools/MLPlatform.SqlGate/bin/Debug/net11.0/mlp-sqlgate.dll "$ROOT"; then ok "SqlGate + CommentGate + migration"; else fail "SqlGate + CommentGate + migration"; fi
if nice -n 19 dotnet build MLPlatform.slnx -m:1 -v q -warnaserror >/dev/null; then ok "build -warnaserror"; else fail "build -warnaserror"; fi
if (cd web && npx tsc --noEmit -p tsconfig.json); then ok "tsc web"; else fail "tsc web"; fi
if [[ " $* " == *" --aot "* ]]; then
  for p in src/Hosts/MLPlatform.Api src/Hosts/MLPlatform.Agent src/Hosts/MLPlatform.Shim; do
    out=$(nice -n 19 dotnet publish "$p" -c Release -m:1 -v q -p:TreatWarningsAsErrors=true -o "$ROOT/.dev/aot/$(basename $p)" 2>&1) \
      || { echo "$out" | grep -E "warning|error" | sort -u | head -20; echo "FAIL gate AOT $p"; exit 1; }
    echo "$out" | grep -qE "IL[23][0-9]{3}" && { echo "$out" | grep -E "IL[23][0-9]{3}" | sort -u | head; echo "FAIL gate AOT $p"; exit 1; }
    ok "AOT $p"
  done
fi
echo "All gates green"
