#!/usr/bin/env bash
# Dev stack for integration scenarios (D-19). Each container is limited to 1 CPU (CLAUDE.md: keep the machine cool).
#   deploy/dev/stack.sh up      — infrastructure via docker compose (compose.test.yaml): local Postgres + S3 for testing ONLY, Ray head; applies migrations
#   deploy/dev/stack.sh full    — the whole stack in docker compose (api + web + agent-builder + infrastructure), builds one image at a time
#   deploy/dev/stack.sh seed    — creates a fixed login account + workspace + machine plug-in command (after `full` or after `test`)
#   deploy/dev/stack.sh builder — builds + runs agent-builder (plug-and-play: builds the agent per machine, cache in .dev/agents)
#   deploy/dev/stack.sh api     — runs the api (background) with dev config; prints the bootstrap token
#   deploy/dev/stack.sh test    — resets identity data, runs the api + all scenarios (needs the toy image)
#   deploy/dev/stack.sh toy     — builds the test recipe image mlp-test/toy-recipe:dev (self-contained linux shim)
#   deploy/dev/stack.sh down    — stops the api/agent and compose services (keeps Postgres unless DOWN_ALL=1)
set -euo pipefail
# Do not leave MSBuild nodes/compiler servers running in the background (they hold the output pipe → script hangs; waste RAM/CPU).
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
STATE="${MLP_DEV_STATE:-$ROOT/.dev}"
mkdir -p "$STATE/s3"
NET=mlp-dev
COMPOSE=(docker compose --project-directory "$ROOT/deploy/compose" -f "$ROOT/deploy/compose/compose.yaml" -f "$ROOT/deploy/compose/compose.dev.yaml")
INFRA=("${COMPOSE[@]}" -f "$ROOT/deploy/compose/compose.test.yaml")   # `up`/`test`: local Postgres + S3 for testing only, Ray without overlay
export COMPOSE_BAKE=false COMPOSE_PARALLEL_LIMIT=1
RAY_IMAGE=rayproject/ray:2.60.0-py312-cpu
S3_IMAGE=chrislusf/seaweedfs:4.54
export MLP__Db__ConnectionString="Host=127.0.0.1;Port=55432;Database=mlp;Username=postgres;Password=dev"
export MLP__Secrets__TokenPepper=dev-pepper
# The agent channel uses TLS in dev too (P0-8): self-signed certificate, the agent pins its SHA-256 (MLP_AGENT_PIN for scenarios).
if [ ! -f "$STATE/agent-cert.pfx" ]; then
  openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=mlp-agent-channel" -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" \
    -keyout "$STATE/agent-cert.key" -out "$STATE/agent-cert.crt" 2>/dev/null
  openssl pkcs12 -export -inkey "$STATE/agent-cert.key" -in "$STATE/agent-cert.crt" -out "$STATE/agent-cert.pfx" -passout pass:dev 2>/dev/null
fi
export MLP__Agent__CertPath="$STATE/agent-cert.pfx" MLP__Agent__CertPassword=dev
export MLP_AGENT_PIN="$(openssl x509 -in "$STATE/agent-cert.crt" -outform der | shasum -a 256 | cut -d' ' -f1)"
export Kestrel__Endpoints__h2__Url=http://0.0.0.0:5080 Kestrel__Endpoints__h2__Protocols=Http2
export Kestrel__Endpoints__web__Url=http://127.0.0.1:5081 Kestrel__Endpoints__web__Protocols=Http1
export MLP__Scheduler__Enabled=true MLP__Scheduler__ApiAddressForRay=host.docker.internal:5080 MLP__Scheduler__DefaultWorkloadNetwork=$NET
export MLP__Ray__AutoStartNodes=true MLP__Ray__HeadAddress=mlp-ray-head:6379 MLP__Ray__NodeNetwork=$NET MLP__Ray__MaxCpusPerNode=1
export MLP__S3__Endpoint=http://127.0.0.1:8333 MLP__S3__AccessKey=mlpdevaccess MLP__S3__SecretKey=mlpdevsecret-change-me
export MLP__S3__PublicEndpoint=http://127.0.0.1:8333 MLP__S3__WorkloadEndpoint=http://mlp-s3-dev:8333
export MLP__PublicUrl=http://localhost:5173
# Login account comes from deploy/compose/.env (the host api creates it too — same as the api in docker)
ENVF="$ROOT/deploy/compose/.env"
export MLP__Admin__Email="$(sed -n 's/^MLP_ADMIN_EMAIL=//p' "$ENVF" | head -1)" MLP__Admin__Password="$(sed -n 's/^MLP_ADMIN_PASSWORD=//p' "$ENVF" | head -1)" MLP__Admin__Name="$(sed -n 's/^MLP_ADMIN_NAME=//p' "$ENVF" | head -1)"
export MLP__Install__AgentDir="$STATE/agents" MLP__Install__ScriptPath="$ROOT/deploy/install/agent.sh" MLP__Install__AgentAddress="${MLP_DEV_AGENT_ADDRESS:-127.0.0.1:7443}"
export MLP__Recovery__GraceSeconds=60   # dev: wait 60 s for a machine to come back (default 300 s, 20/10 §10)
export MLP__Recovery__SignalLostSeconds=30 MLP__Recovery__SignalKillMinutes=2   # dev: signal lost after 30 s (default 120 s / 30 minutes)
export MLP__Serving__PublishToHost=true   # dev: macOS does not route to container IPs → publish replica ports on 127.0.0.1

# MLP_DEV_LAN=<ip>: expose the dev control plane to real machines on the LAN (plug in real Windows/Linux agents) — web/install listen on 0.0.0.0.
if [ -n "${MLP_DEV_LAN:-}" ]; then
  export Kestrel__Endpoints__web__Url=http://0.0.0.0:5081 MLP__PublicUrl=http://$MLP_DEV_LAN:5081 MLP__Install__AgentAddress=$MLP_DEV_LAN:7443
  export MLP_DEV_BIND=0.0.0.0 MLP__S3__WorkloadEndpoint=http://$MLP_DEV_LAN:8333 MLP__S3__PublicEndpoint=http://$MLP_DEV_LAN:8333
  export MLP__Scheduler__ApiAddressForRay=$MLP_DEV_LAN:5080
fi
# MLP_DEV_RAY_HOST=<ip>: use a Ray head running on another machine (e.g. a Windows GPU machine — head + worker share a docker network there, no overlay needed).
if [ -n "${MLP_DEV_RAY_HOST:-}" ]; then
  export MLP__Ray__DashboardUrl=http://$MLP_DEV_RAY_HOST:8265/
fi

recipes_dir() {   # builtin recipes + toy fixture (test environment only)
  local d="$STATE/recipes"; rm -rf "$d"; mkdir -p "$d"
  for r in "$ROOT"/recipes/*; do ln -s "$r" "$d/"; done
  ln -s "$ROOT/tools/MLPlatform.Scenarios/fixtures/toy-recipe" "$d/toy-recipe"
  ln -s "$ROOT/tools/MLPlatform.Scenarios/fixtures/gpu-recipe" "$d/gpu-recipe"
  echo "$d"
}

up() {
  # Old containers created with `docker run` (before the move to compose) clash by name → remove once.
  for c in mlp-pg-dev mlp-s3-dev mlp-ray-head; do
    [ "$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project"}}' $c 2>/dev/null)" = "" ] && docker rm -f $c >/dev/null 2>&1 || true
  done
  "${COMPOSE[@]}" rm -sf api web wireguard >/dev/null 2>&1 || true
  docker rm -f mlp-ray-head >/dev/null 2>&1 || true   # may be in the wireguard namespace (full mode)
  "${INFRA[@]}" up -d postgres s3 ray-head
  for i in $(seq 1 30); do docker exec mlp-pg-dev pg_isready -U postgres -d mlp >/dev/null 2>&1 && break; sleep 2; done
  for i in $(seq 1 60); do curl -s -o /dev/null http://127.0.0.1:8333/ && break; sleep 2; done   # S3 (SeaweedFS) is slow to start
  for i in $(seq 1 30); do curl -sf http://127.0.0.1:8265/api/version >/dev/null && break; sleep 2; done
  (cd "$ROOT" && nice -n 19 dotnet build src/Hosts/MLPlatform.Api -m:1 -v q >/dev/null)
  (cd "$ROOT" && nice -n 19 dotnet run --project src/Hosts/MLPlatform.Api --no-build -- db apply --root "$ROOT/db")
}

# Whole stack in docker (AOT api + web + agent-builder + infrastructure). Builds one image at a time (api ~10–15 minutes the first time, web ~3–5 minutes).
full() {
  pkill -f MLPlatform.Api || true
  for svc in wireguard api web agent-builder; do nice -n 19 "${COMPOSE[@]}" build $svc || return 1; done
  docker rm -f mlp-ray-head >/dev/null 2>&1 || true   # ray-head moves into wireguard's network namespace (overlay)
  rm -rf "$STATE/recipes-docker"; mkdir -p "$STATE/recipes-docker" "$STATE/agents"   # builtin recipes + GPU recipe for testing real machines (copies)
  cp -R "$ROOT"/recipes/* "$ROOT/tools/MLPlatform.Scenarios/fixtures/gpu-recipe" "$STATE/recipes-docker/"
  "${COMPOSE[@]}" up -d wireguard ray-head certs-init db-migrate agent-builder api web   # DB/S3 = external infrastructure from .env
  for i in $(seq 1 60); do curl -sf http://127.0.0.1:5081/health/ready >/dev/null && break; sleep 3; done
  curl -sf http://127.0.0.1:5081/health/ready && echo " — api ready; web http://127.0.0.1:8080"
  docker logs mlp-api-dev 2>&1 | grep -o "mlpb_[a-z0-9]*" | tail -1 > "$STATE/token" || true
}

# Fixed account for trying the UI (`test` wipes identity data → rerun seed after each test).
# MLP_ADMIN_EMAIL / MLP_ADMIN_PASSWORD (default admin@lab.local / mot-mat-khau-dai — dev environment only).
seed() {
  # Local test DB only — never wipes data on a real PostgreSQL (the `full` stack creates the admin from .env itself).
  docker ps --format '{{.Names}}' | grep -qx mlp-pg-dev || { echo "seed only runs against the test infrastructure (stack.sh up). Full stack: sign in with MLP_ADMIN_* from .env"; return 1; }
  local api_name; api_name=$(docker ps --format '{{.Names}}' | grep -x mlp-api-dev || true)
  docker exec mlp-pg-dev psql -U postgres -d mlp -qc "TRUNCATE identity.memberships, identity.workspaces, identity.sessions, identity.users CASCADE; UPDATE identity.instance_settings SET bootstrapped_at=NULL;" >/dev/null
  if [ -n "$api_name" ]; then
    docker restart mlp-api-dev >/dev/null
    for i in $(seq 1 60); do curl -sf http://127.0.0.1:5081/health/ready >/dev/null && break; sleep 2; done
    docker logs mlp-api-dev 2>&1 | grep -o "mlpb_[a-z0-9]*" | tail -1 > "$STATE/token"
  else api >/dev/null; fi
  (cd "$ROOT" && nice -n 19 dotnet build tools/MLPlatform.Scenarios -m:1 -v q >/dev/null)
  dotnet "$ROOT/tools/MLPlatform.Scenarios/bin/Debug/net10.0/mlp-scenarios.dll" http://127.0.0.1:5080 "$(cat "$STATE/token")" enroll "${SEED_OS:-windows}" 'C:\mlp\datasets' 'C:\mlp\models' \
    | grep -v '^SESSION' | tee "$STATE/enroll.txt"
  echo "Sign in: ${MLP_ADMIN_EMAIL:-admin@lab.local} / ${MLP_ADMIN_PASSWORD:-mot-mat-khau-dai}"
}

builder() {   # one image, CPU limited in compose.dev.yaml
  mkdir -p "$STATE/agents"
  nice -n 19 "${COMPOSE[@]}" up -d --build agent-builder
}

api() {
  pkill -f mlp-api || true
  export MLP__Catalog__RecipesDir="$(recipes_dir)"
  rm -f "$STATE/api.log" "$STATE/token"
  # The background process must not hold the calling shell's stdin/stdout (avoids hanging when called inside $(...)).
  (cd "$ROOT" && nohup nice -n 19 dotnet run --project src/Hosts/MLPlatform.Api --no-build </dev/null > "$STATE/api.log" 2>&1 &)
  for i in $(seq 1 40); do grep -q "Application started" "$STATE/api.log" 2>/dev/null && break; sleep 1; done
  grep -o "mlpb_[a-z0-9]*" "$STATE/api.log" | tail -1 > "$STATE/token" || true
  cat "$STATE/token"
}

test_all() {
  docker exec mlp-pg-dev psql -U postgres -d mlp -qc "TRUNCATE identity.memberships, identity.workspaces, identity.sessions, identity.users, sched.jobs CASCADE; UPDATE identity.instance_settings SET bootstrapped_at=NULL;"
  MLP__Admin__Email="" api >/dev/null   # scenarios need an uninitialized instance (tests /setup); on a normal start the api recreates the .env account
  local token; token="$(cat "$STATE/token")"
  (cd "$ROOT" && nice -n 19 dotnet build tools/MLPlatform.Scenarios -m:1 -v q >/dev/null && nice -n 19 dotnet build src/Hosts/MLPlatform.Agent -m:1 -v q >/dev/null)
  MLP_RAY=1 MLP_AGENT="$ROOT/src/Hosts/MLPlatform.Agent/bin/Debug/net10.0/mlp-agent.dll" \
    nice -n 19 dotnet run --project "$ROOT/tools/MLPlatform.Scenarios" --no-build -- http://127.0.0.1:5080 "$token"
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
  pkill -f mlp-api || true; pkill -f mlp-agent || true
  "${COMPOSE[@]}" rm -sf api web wireguard s3 ray-head agent-builder >/dev/null 2>&1 || true
  docker ps -aq --filter label=mlp.managed=1 | xargs -r docker rm -f >/dev/null 2>&1 || true
  [ "${DOWN_ALL:-0}" = 1 ] && "${COMPOSE[@]}" down >/dev/null 2>&1 || true
}

case "${1:-}" in
  up) up ;; full) full ;; seed) seed ;; builder) builder ;; api) api ;; test) test_all ;; toy) toy ;; down) down ;;
  *) echo "Usage: $0 up|full|seed|builder|api|test|toy|down"; exit 2 ;;
esac
