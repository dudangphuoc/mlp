#!/usr/bin/env bash
# Ray head for the control plane, trimmed: the platform uses only the job API (/api/jobs, /api/version), the state API (nodes, health)
# and cluster status. The full dashboard starts ~12 Python processes (Train, Serve, Data, Metrics, Event… ≈150 MB each) nobody uses —
# raydash/sitecustomize.py makes the dashboard load just the modules below. Usage stats are off.
set -euo pipefail
export PYTHONPATH="/opt/mlp/mlp_holder/raydash${PYTHONPATH:+:$PYTHONPATH}"
export MLP_RAY_DASHBOARD_MODULES="JobHead,NodeHead,StateHead,ReportHead"   # NodeHead: JobHead finds node agents through it
exec ray start --head --block --num-cpus=0 --num-gpus=0 --disable-usage-stats --dashboard-host=0.0.0.0 --port=6379 \
  --node-ip-address="${MLP_OVERLAY_IP:-10.88.0.1}"
