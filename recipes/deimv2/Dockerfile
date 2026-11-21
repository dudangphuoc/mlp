# deimv2 — build context: recipes/  (docker build -f deimv2/Dockerfile recipes/)
# LICENSE: non-commercial only (DEIMv2 License, since 2026-08-24). requirements pin torch==2.5.1.
FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime

RUN apt-get update && apt-get install -y --no-install-recommends git curl libgl1 libglib2.0-0 && rm -rf /var/lib/apt/lists/*
ARG DEIMV2_COMMIT=f005f50ce565e63f6c298feccb51901a98c3d035
RUN git clone https://github.com/Intellindust-AI-Lab/DEIMv2.git /opt/recipe/src \
    && cd /opt/recipe/src && git checkout ${DEIMV2_COMMIT} \
    && pip install --no-cache-dir -r requirements.txt \
    && pip install --no-cache-dir opencv-python-headless onnx onnxsim gdown
# HGNetv2 backbones for Atto/Femto/Pico/N into ./weight/hgnetv2/
RUN mkdir -p /opt/recipe/src/weight/hgnetv2 && cd /opt/recipe/src/weight/hgnetv2 \
    && for b in 0 1 2 3 4 5 6; do curl -fsSL -O https://github.com/Peterande/storage/releases/download/dfinev1.0/PPHGNetV2_B${b}_stage1.pth; done

COPY _shared/detr_family/ /opt/recipe/shared/
RUN touch /opt/recipe/shared/__init__.py
WORKDIR /opt/recipe/src
