#!/usr/bin/env bash
# Isolated verification stack for refactors: Postgres + S3 + Ray in compose project "mlp-verify", api from source on 5090/5091/7453.
#   deploy/verify/verify.sh up     — start infrastructure, build api, apply migrations
#   deploy/verify/verify.sh test   — reset identity data, start the api, run every integration scenario
#   deploy/verify/verify.sh down   — stop the api/agent started here and remove the mlp-verify project (volumes kept unless DOWN_ALL=1)
# Never touches the dev stack: different project, network, container names, ports and state folder.
set -euo pipefail
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
STATE="$ROOT/.dev/verify"; mkdir -p "$STATE"
COMPOSE=(docker compose -p mlp-verify -f "$ROOT/deploy/verify/compose.yaml")
export MLP__Db__ConnectionString="Host=127.0.0.1;Port=55433;Database=mlp;Username=postgres;Password=verify"
export MLP__Secrets__TokenPepper=verify-pepper
if [ ! -f "$STATE/agent-cert.pfx" ]; then
  openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=mlp-verify" -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" \
    -keyout "$STATE/agent-cert.key" -out "$STATE/agent-cert.crt" 2>/dev/null
  openssl pkcs12 -export -inkey "$STATE/agent-cert.key" -in "$STATE/agent-cert.crt" -out "$STATE/agent-cert.pfx" -passout pass:verify 2>/dev/null
fi
export MLP__Agent__CertPath="$STATE/agent-cert.pfx" MLP__Agent__CertPassword=verify MLP__Agent__Port=7453
export MLP_AGENT_PIN="$(openssl x509 -in "$STATE/agent-cert.crt" -outform der | shasum -a 256 | cut -d' ' -f1)"
export Kestrel__Endpoints__h2__Url=http://0.0.0.0:5090 Kestrel__Endpoints__h2__Protocols=Http2
export Kestrel__Endpoints__web__Url=http://127.0.0.1:5091 Kestrel__Endpoints__web__Protocols=Http1
export MLP__Scheduler__Enabled=true MLP__Scheduler__ApiAddressForRay=host.docker.internal:5090 MLP__Scheduler__DefaultWorkloadNetwork=mlp-verify
export MLP__Ray__AutoStartNodes=true MLP__Ray__HeadAddress=mlp-verify-ray:6379 MLP__Ray__NodeNetwork=mlp-verify MLP__Ray__MaxCpusPerNode=1
export MLP__Ray__DashboardUrl=http://127.0.0.1:8266/
export MLP__S3__Endpoint=http://127.0.0.1:8433 MLP__S3__AccessKey=verifyaccess MLP__S3__SecretKey=verifysecret-not-real
export MLP__S3__PublicEndpoint=http://127.0.0.1:8433 MLP__S3__WorkloadEndpoint=http://mlp-verify-s3:8333
export MLP__PublicUrl=http://127.0.0.1:5091
export MLP__Install__AgentDir="$STATE/agents" MLP__Install__ScriptPath="$ROOT/deploy/install/agent.sh" MLP__Install__AgentAddress=127.0.0.1:7453
export MLP__Recovery__GraceSeconds=60 MLP__Recovery__SignalLostSeconds=30 MLP__Recovery__SignalKillMinutes=2
export MLP__Serving__PublishToHost=true
API_DLL="$ROOT/src/Hosts/MLPlatform.Api/bin/Debug/net11.0/mlp-api.dll"

recipes_dir() {
  local d="$STATE/recipes"; rm -rf "$d"; mkdir -p "$d"
  for r in "$ROOT"/recipes/*; do ln -s "$r" "$d/"; done
  ln -s "$ROOT/tools/MLPlatform.Scenarios/fixtures/toy-recipe" "$d/toy-recipe"
  ln -s "$ROOT/tools/MLPlatform.Scenarios/fixtures/gpu-recipe" "$d/gpu-recipe"
  echo "$d"
}

stop_api() { pkill -f "$API_DLL" 2>/dev/null || true; pkill -f "mlp-agent.dll.*7453" 2>/dev/null || true; }

up() {
  nice -n 19 "${COMPOSE[@]}" up -d
  for i in $(seq 1 30); do docker exec mlp-verify-pg pg_isready -U postgres -d mlp >/dev/null 2>&1 && break; sleep 2; done
  for i in $(seq 1 60); do curl -s -o /dev/null http://127.0.0.1:8433/ && break; sleep 2; done
  for i in $(seq 1 30); do curl -sf http://127.0.0.1:8266/api/version >/dev/null && break; sleep 2; done
  (cd "$ROOT" && nice -n 19 dotnet build src/Hosts/MLPlatform.Api -m:1 -v q >/dev/null)
  nice -n 19 dotnet "$API_DLL" db apply --root "$ROOT/db"
}

start_api() {
  stop_api
  export MLP__Catalog__RecipesDir="$(recipes_dir)"
  rm -f "$STATE/api.log" "$STATE/token"
  (nohup nice -n 19 dotnet "$API_DLL" </dev/null > "$STATE/api.log" 2>&1 &)
  for i in $(seq 1 60); do grep -q "Application started" "$STATE/api.log" 2>/dev/null && break; sleep 1; done
  grep -o "mlpb_[a-z0-9]*" "$STATE/api.log" | tail -1 > "$STATE/token" || true
}

test_all() {
  docker exec mlp-verify-pg psql -U postgres -d mlp -qc "TRUNCATE identity.memberships, identity.workspaces, identity.sessions, identity.users, sched.jobs CASCADE; UPDATE identity.instance_settings SET bootstrapped_at=NULL;"
  (cd "$ROOT" && nice -n 19 dotnet build src/Hosts/MLPlatform.Api -m:1 -v q >/dev/null \
    && nice -n 19 dotnet build tools/MLPlatform.Scenarios -m:1 -v q >/dev/null && nice -n 19 dotnet build src/Hosts/MLPlatform.Agent -m:1 -v q >/dev/null)
  MLP__Admin__Email="" start_api
  local token; token="$(cat "$STATE/token")"
  [ -n "$token" ] || { echo "api did not print a bootstrap token; see $STATE/api.log"; tail -20 "$STATE/api.log"; return 1; }
  local rc=0
  MLP_RAY=1 MLP_AGENT="$ROOT/src/Hosts/MLPlatform.Agent/bin/Debug/net11.0/mlp-agent.dll" MLP_AGENT_CP=127.0.0.1:7453 MLP_TEST_NETWORK=mlp-verify MLP_WEB_URL=http://127.0.0.1:5091 \
    nice -n 19 dotnet "$ROOT/tools/MLPlatform.Scenarios/bin/Debug/net11.0/mlp-scenarios.dll" http://127.0.0.1:5090 "$token" || rc=$?
  stop_api
  return $rc
}

down() {
  stop_api
  docker ps -aq --filter network=mlp-verify --filter label=mlp.managed=1 | xargs -r docker rm -f >/dev/null 2>&1 || true
  if [ "${DOWN_ALL:-0}" = 1 ]; then "${COMPOSE[@]}" down -v; else "${COMPOSE[@]}" down; fi
}

case "${1:-}" in
  up) up ;; test) test_all ;; down) down ;;
  *) echo "Usage: $0 up|test|down"; exit 2 ;;
esac
