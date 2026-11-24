# G0 prototype: F5 debug of a script running in a remote container

Throwaway prototype for `docs/recipe-source-sdk-plan.md` §4.2. A Docker container on this machine plays the GPU host; a TCP relay inside the SDK plays the api tunnel.

- `proj/mlplatform_proto/__init__.py` — `init()`: reads the IDE's debug adapter address and token from `pydevd.SetupHolder.setup`, starts a relay, runs the same script in a container under `debugpy --connect … --parent-session-pid <local pid>`, streams output, exits with the remote exit code.
- `dap_client.py` — headless stand-in for VS Code: starts `debugpy.adapter`, sends `launch` (F5), answers `startDebugging`, sets breakpoints, checks stops.

```bash
export MLP_PROTO_DEBUGPY=$(ls -d ~/.vscode/extensions/ms-python.debugpy-*/bundled/libs | tail -1)
python3 dap_client.py full        # also: nodebug, terminate, replicas, mp
```

Env switches: `MLP_PROTO_REPLICAS`, `MLP_PROTO_REWRITE_PID=1`, `MLP_PROTO_DELAY_MS`, `MLP_PROTO_SCRIPT=train_mp.py`, `MLP_PROTO_LOGS=<dir>`.
