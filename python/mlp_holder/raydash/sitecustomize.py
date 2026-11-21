# Loaded by every Python process of the Ray head (PYTHONPATH), acts only inside the dashboard process: the dashboard loads just the
# modules named in MLP_RAY_DASHBOARD_MODULES instead of all of them (Train, Serve, Data, Metrics, Event… ≈150 MB each, unused here).
import os
import sys

_keep = {m for m in os.environ.get("MLP_RAY_DASHBOARD_MODULES", "").split(",") if m}
if _keep and sys.argv and sys.argv[0].endswith(os.path.join("dashboard", "dashboard.py")):
    try:
        import ray.dashboard.head as _head

        _orig = _head.DashboardHead._load_modules

        def _load_modules(self, modules_to_load=None):
            return _orig(self, _keep)

        _head.DashboardHead._load_modules = _load_modules
    except Exception as e:  # never stop the dashboard over this
        print(f"[mlp] dashboard module filter not applied: {e}", file=sys.stderr)
