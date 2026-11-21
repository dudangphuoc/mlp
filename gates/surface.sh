#!/usr/bin/env bash
# Compares the api surface (routes, gRPC methods, DI registrations, requests) with artifacts/refactor/surface-baseline.json.
#   gates/surface.sh           — diff against the baseline (fails on any difference)
#   gates/surface.sh --accept  — accept the current surface as the new baseline (after reviewing the diff)
set -euo pipefail
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; cd "$ROOT"
OUT="$(mktemp -t mlp-surface).json"
nice -n 19 dotnet build src/Hosts/MLPlatform.Api -m:1 -v q >/dev/null
env -u MLP__Db__ConnectionString dotnet src/Hosts/MLPlatform.Api/bin/Debug/net11.0/mlp-api.dll describe "$OUT" >/dev/null
if [[ " $* " == *" --accept "* ]]; then cp "$OUT" artifacts/refactor/surface-baseline.json; echo "surface baseline updated"; exit 0; fi
if diff -u artifacts/refactor/surface-baseline.json "$OUT"; then echo "OK   surface unchanged"; else echo "FAIL surface changed (review, then gates/surface.sh --accept)"; exit 1; fi
