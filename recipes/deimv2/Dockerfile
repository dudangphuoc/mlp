# deimv2 — build context: recipes/  (docker build -f deimv2/Dockerfile recipes/)
# LICENSE: non-commercial only (DEIMv2 License, since 2026-08-24). requirements pin torch==2.5.1.
FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime

RUN apt-get update && apt-get install -y --no-install-recommends git curl libgl1 libglib2.0-0 && rm -rf /var/lib/apt/lists/*
ARG DEIMV2_COMMIT=f005f50ce565e63f6c298feccb51901a98c3d035
COPY deimv2/constraints.txt /opt/recipe/constraints.txt
ENV PIP_CONSTRAINT=/opt/recipe/constraints.txt
RUN git clone https://github.com/Intellindust-AI-Lab/DEIMv2.git /opt/recipe/src \
    && cd /opt/recipe/src && git checkout ${DEIMV2_COMMIT} \
    && pip install --no-cache-dir -r requirements.txt \
    && pip install --no-cache-dir opencv-python-headless onnx onnxsim gdown
# HGNetv2 backbones for Atto/Femto/Pico/N into ./weight/hgnetv2/
RUN mkdir -p /opt/recipe/src/weight/hgnetv2 && cd /opt/recipe/src/weight/hgnetv2 \
    && for b in 0 1 2 3 4 5 6; do curl -fsSL -O https://github.com/Peterande/storage/releases/download/dfinev1.0/PPHGNetV2_B${b}_stage1.pth; done \
    && printf '%s\n' '70a372e8cbc59b34c5da2943261ecb633faf304a58e7e05461a27bd8d8b7f3d1  PPHGNetV2_B0_stage1.pth' '3195a3fbd853af42c0c06eb121338077e96a5f7580daf503a4c05d4fc84b7fb9  PPHGNetV2_B1_stage1.pth' '41272985db6136ac11732b246c7ea794dcc203d0a8cbc463152c840b9d9f22d1  PPHGNetV2_B2_stage1.pth' '6be537deffd46d1c86e1a9316d79439cda9cebe9984fea91f486bdea4e716840  PPHGNetV2_B3_stage1.pth' 'a72ad8d32902c90f5fa07f642034955d0ed9149c46d4d97f0e2ec36344d24bea  PPHGNetV2_B4_stage1.pth' '812d5cde50e415abfb1ea1dd27121fa4f861522a327e48033a3cea8d604b3545  PPHGNetV2_B5_stage1.pth' '231b0078840733f4f9b91076e5fb9487a75a14eeaa0aa16a5b0e826cc6844731  PPHGNetV2_B6_stage1.pth' | sha256sum -c -

COPY _shared/detr_family/ /opt/recipe/shared/
RUN touch /opt/recipe/shared/__init__.py
WORKDIR /opt/recipe/src
