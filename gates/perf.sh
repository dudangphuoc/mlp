#!/usr/bin/env bash
# Measures the Native AOT api on the isolated verify stack: startup time, idle RSS, /health/ready latency (p50/p95).
#   gates/perf.sh <label>   — publishes the api (AOT, nice), runs it against mlp-verify, appends a JSON line to artifacts/refactor/perf.jsonl
set -euo pipefail
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; cd "$ROOT"
LABEL="${1:-run}"
OUT="$ROOT/.dev/perf/api"
nice -n 19 dotnet publish src/Hosts/MLPlatform.Api -c Release -m:1 -v q -o "$OUT" >/dev/null
export MLP__Db__ConnectionString="Host=127.0.0.1;Port=55433;Database=mlp;Username=postgres;Password=verify" MLP__Secrets__TokenPepper=verify-pepper
export Kestrel__Endpoints__web__Url=http://127.0.0.1:5095 Kestrel__Endpoints__web__Protocols=Http1 MLP__Agent__AllowPlaintext=true MLP__Agent__Port=7459
start=$(python3 -c 'import time;print(time.time())')
"$OUT/mlp-api" > "$ROOT/.dev/perf/api.log" 2>&1 &
pid=$!
trap 'kill $pid 2>/dev/null || true' EXIT
until curl -sf http://127.0.0.1:5095/health/ready >/dev/null 2>&1; do sleep 0.05; done
ready=$(python3 -c "import time;print(round((time.time()-$start)*1000))")
sleep 3
rss=$(ps -o rss= -p $pid | awk '{print int($1/1024)}')
lat=$(for i in $(seq 1 300); do curl -s -o /dev/null -w '%{time_total}\n' http://127.0.0.1:5095/health/ready; done | sort -n | awk '{a[NR]=$1} END {printf "%.2f %.2f", a[int(NR*0.5)]*1000, a[int(NR*0.95)]*1000}')
size=$(du -m "$OUT/mlp-api" | cut -f1)
printf '{"label":"%s","at":"%s","startup_ms":%s,"rss_mb":%s,"ready_p50_ms":%s,"ready_p95_ms":%s,"binary_mb":%s}\n' "$LABEL" "$(date -u +%FT%TZ)" "$ready" "$rss" ${lat% *} ${lat#* } "$size" | tee -a artifacts/refactor/perf.jsonl
