# vibevoice-realtime — per docs/vibevoice-realtime-0.5b.md (NVIDIA PyTorch 24.07/24.10/24.12 containers tested).
FROM nvcr.io/nvidia/pytorch:24.12-py3

RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg git && rm -rf /var/lib/apt/lists/*
ARG VIBEVOICE_COMMIT=16fb2cb1217c9934a886e1948ffb06120caa2df5
COPY vibevoice-realtime/constraints.txt /opt/recipe/constraints.txt
ENV PIP_CONSTRAINT=/opt/recipe/constraints.txt
RUN git clone https://github.com/microsoft/VibeVoice.git /opt/recipe/src \
    && cd /opt/recipe/src && git checkout ${VIBEVOICE_COMMIT} \
    && pip install --no-cache-dir -e ".[streamingtts]"
RUN python -c "import flash_attn" 2>/dev/null || pip install --no-cache-dir flash-attn --no-build-isolation || true

WORKDIR /opt/recipe
COPY vibevoice-realtime/handler.py ./
