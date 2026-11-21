# Notebook image (M08, P0-9) — the control plane sends this Dockerfile to the chosen machine, which builds it locally the first time (ImageBuild) and reuses it.
# BASE: python slim (CPU) or PyTorch CUDA runtime (GPU); PIP: packages per profile.
ARG BASE=python:3.12-slim
FROM ${BASE}
ARG PIP="jupyterlab"
ENV PIP_NO_CACHE_DIR=1 PYTHONUNBUFFERED=1
RUN (command -v apt-get >/dev/null && apt-get update && apt-get install -y --no-install-recommends git curl && rm -rf /var/lib/apt/lists/*) || true \
 && pip install --upgrade pip && pip install ${PIP} \
 && (id -u mlp >/dev/null 2>&1 || useradd -m -u 1000 -s /bin/bash mlp || adduser --disabled-password --uid 1000 mlp) \
 && mkdir -p /home/mlp && chown -R 1000:1000 /home/mlp && chmod 777 /home/mlp
# JupyterLab UI matching MLPlatform: dark theme, no Jupyter news prompt/fetch, no update checks (no popups).
RUN mkdir -p /usr/local/share/jupyter/lab/settings \
 && printf '%s' '{"@jupyterlab/apputils-extension:themes":{"theme":"JupyterLab Dark","theme-scrollbars":true},"@jupyterlab/apputils-extension:notification":{"fetchNews":"false","checkForUpdates":false,"doNotDisturbMode":false}}' \
    > /usr/local/share/jupyter/lab/settings/overrides.json \
 && PREFIX=$(python -c 'import sys;print(sys.prefix)') \
 && if [ "$PREFIX" != /usr/local ]; then mkdir -p "$PREFIX/share/jupyter/lab/settings" && cp /usr/local/share/jupyter/lab/settings/overrides.json "$PREFIX/share/jupyter/lab/settings/"; fi
# Jupyter does not run as root; the (per-user) home volume inherits this directory's ownership on first mount.
USER mlp
ENV HOME=/home/mlp
WORKDIR /home/mlp
EXPOSE 8888
LABEL io.mlplatform.image=notebook
