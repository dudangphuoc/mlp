#!/usr/bin/env bash
# Isolated verification stack for refactors: Postgres + S3 + Ray in compose project "mlp-verify", api from source on 5090/5091/7453.
#   deploy/verify/verify.sh up     — start infrastructure, build api, apply migrations
#   deploy/verify/verify.sh test   — reset identity data, start the api, run every integration scenario
#   deploy/verify/verify.sh toy    — rebuild the test recipe image mlp-test/toy-recipe:dev from the current shim and fixtures
#   deploy/verify/verify.sh api    — start the api for UI checks (admin account from deploy/compose/.env)
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

remove_stale_workloads() {
  docker ps -aq --filter network=mlp-verify | while read -r id; do
    name=$(docker inspect -f '{{.Name}}' "$id" | tr -d /); [[ $name == mlp-verify-* ]] || docker rm -f "$id" >/dev/null 2>&1 || true
  done
}

test_all() {
  remove_stale_workloads
  docker exec mlp-verify-pg psql -U postgres -d mlp -qc "TRUNCATE identity.memberships, identity.workspaces, identity.sessions, identity.users, sched.jobs CASCADE; UPDATE identity.instance_settings SET bootstrapped_at=NULL;"
  (cd "$ROOT" && nice -n 19 dotnet build src/Hosts/MLPlatform.Api -m:1 -v q -clp:ErrorsOnly \
    && nice -n 19 dotnet build tools/MLPlatform.Scenarios -m:1 -v q -clp:ErrorsOnly && nice -n 19 dotnet build src/Hosts/MLPlatform.Agent -m:1 -v q -clp:ErrorsOnly)
  MLP__Admin__Email="" start_api
  local token; token="$(cat "$STATE/token")"
  [ -n "$token" ] || { echo "api did not print a bootstrap token; see $STATE/api.log"; tail -20 "$STATE/api.log"; return 1; }
  local rc=0
  MLP_RAY=1 MLP_AGENT="$ROOT/src/Hosts/MLPlatform.Agent/bin/Debug/net11.0/mlp-agent.dll" MLP_AGENT_CP=127.0.0.1:7453 MLP_TEST_NETWORK=mlp-verify MLP_WEB_URL=http://127.0.0.1:5091 \
    nice -n 19 dotnet "$ROOT/tools/MLPlatform.Scenarios/bin/Debug/net11.0/mlp-scenarios.dll" http://127.0.0.1:5090 "$token" || rc=$?
  stop_api
  return $rc
}

toy() {
  local out="$STATE/toyimg"; rm -rf "$out"; mkdir -p "$out"
  local arch; arch=$(docker info --format '{{.Architecture}}'); local rid=linux-x64; [ "$arch" = aarch64 ] && rid=linux-arm64
  (cd "$ROOT" && nice -n 19 dotnet publish src/Hosts/MLPlatform.Shim -c Release -r $rid --self-contained -p:PublishAot=false \
    -p:PublishSingleFile=true -m:1 -o "$out/shim" >/dev/null)
  cp "$out/shim/mlp-shim" "$out/"; cp -r "$ROOT/tools/MLPlatform.Scenarios/fixtures/toy-recipe" "$out/src"
  cp -r "$ROOT/python/mlp_serve" "$out/mlp_serve"; cp -r "$ROOT/proto" "$out/proto"
  cp "$ROOT/tools/MLPlatform.Scenarios/fixtures/fake-jupyter/jupyter" "$out/jupyter"   # M08: fake Jupyter (HTTP + WebSocket)
  # Final layer of every recipe image (20/08 §6): mlp-shim + mlp-serve (gRPC stubs generated at build time, matching the image's grpcio version)
  cat > "$out/Dockerfile" <<'DOCKER'
FROM rayproject/ray:2.60.0-py312-cpu
USER root
COPY mlp-shim /usr/local/bin/mlp-shim
COPY jupyter /usr/local/bin/jupyter
COPY src /opt/recipe/src
COPY mlp_serve /opt/mlp-py/mlp_serve
COPY proto /opt/mlp-py/proto
RUN chmod +x /usr/local/bin/mlp-shim /usr/local/bin/jupyter && mkdir -p /mnt/output /mnt/data /mnt/model && chmod 777 /mnt/output /mnt/data /mnt/model \
 && pip install --no-cache-dir "grpcio-tools==$(python -c 'import grpc;print(grpc.__version__)')" \
 && mkdir -p /opt/mlp-py/mlp_serve/gen && cd /opt/mlp-py \
 && python -m grpc_tools.protoc -Iproto --python_out=mlp_serve/gen --grpc_python_out=mlp_serve/gen proto/mlplatform/v1/model_server.proto \
 && sed -i 's/^from mlplatform.v1 import/from mlp_serve.gen.mlplatform.v1 import/' mlp_serve/gen/mlplatform/v1/model_server_pb2_grpc.py \
 && touch mlp_serve/gen/__init__.py mlp_serve/gen/mlplatform/__init__.py mlp_serve/gen/mlplatform/v1/__init__.py \
 && printf '#!/bin/sh\nexec python -m mlp_serve.server "$@"\n' > /usr/local/bin/mlp-serve && chmod +x /usr/local/bin/mlp-serve \
 && cp /opt/recipe/src/handler.py /opt/recipe/handler.py
ENV PYTHONPATH=/opt/mlp-py
DOCKER
  nice -n 19 docker build -q -t mlp-test/toy-recipe:dev "$out"
}

down() {
  stop_api
  docker ps -aq --filter network=mlp-verify --filter label=mlp.managed=1 | xargs -r docker rm -f >/dev/null 2>&1 || true
  if [ "${DOWN_ALL:-0}" = 1 ]; then "${COMPOSE[@]}" down -v; else "${COMPOSE[@]}" down; fi
}

api_for_ui() {
  local envf="$ROOT/deploy/compose/.env"
  export MLP__Admin__Email="$(sed -n 's/^MLP_ADMIN_EMAIL=//p' "$envf" | head -1)" MLP__Admin__Password="$(sed -n 's/^MLP_ADMIN_PASSWORD=//p' "$envf" | head -1)"
  (cd "$ROOT" && nice -n 19 dotnet build src/Hosts/MLPlatform.Api -m:1 -v q -clp:ErrorsOnly)
  start_api
  echo "api on http://127.0.0.1:5091 (web: MLP_API_WEB=http://127.0.0.1:5091)"
}

case "${1:-}" in
  up) up ;; test) test_all ;; toy) toy ;; api) api_for_ui ;; down) down ;;
  *) echo "Usage: $0 up|test|down"; exit 2 ;;
esac
