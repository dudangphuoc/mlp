# d-fine — build context: the recipes/ directory  (docker build -f d-fine/Dockerfile recipes/)
# torch 2.5.1: avoids the torch.load(weights_only=True) default change in 2.6 (checkpoints contain optimizer/EMA).
FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime

RUN apt-get update && apt-get install -y --no-install-recommends git curl libgl1 libglib2.0-0 && rm -rf /var/lib/apt/lists/*
ARG DFINE_COMMIT=956d1709314c2c6a4df6f34de232054578a7449f
COPY d-fine/constraints.txt /opt/recipe/constraints.txt
ENV PIP_CONSTRAINT=/opt/recipe/constraints.txt
RUN git clone https://github.com/Peterande/D-FINE.git /opt/recipe/src \
    && cd /opt/recipe/src && git checkout ${DFINE_COMMIT} \
    && pip install --no-cache-dir -r requirements.txt \
    # missing from requirements: matplotlib (imported by train), opencv (inference), onnx/onnxsim (export always simplifies)
    # transformers (pulled in by calflops) ≥ 5 needs torch ≥ 2.6 and fails on import with 2.5.1 → pin the last 4.x line that supports it
    # pycocotools: recent faster-coco-eval imports it at evaluation (ModuleNotFoundError on the first training run otherwise)
    && pip install --no-cache-dir matplotlib opencv-python-headless onnx onnxsim "transformers==4.48.3" pycocotools \
    && python -c "import faster_coco_eval, pycocotools, calflops, transformers, loguru, scipy, cv2; print('d-fine deps ok')"

# Pre-download the HGNetv2 backbone into weight/hgnetv2/ so it is not downloaded during training (avoids failures with multiple GPUs/no network)
RUN mkdir -p /opt/recipe/src/weight/hgnetv2 && cd /opt/recipe/src/weight/hgnetv2 \
    && for b in 0 1 2 3 4 5 6; do curl -fsSL -O https://github.com/Peterande/storage/releases/download/dfinev1.0/PPHGNetV2_B${b}_stage1.pth; done \
    && printf '%s\n' '70a372e8cbc59b34c5da2943261ecb633faf304a58e7e05461a27bd8d8b7f3d1  PPHGNetV2_B0_stage1.pth' '3195a3fbd853af42c0c06eb121338077e96a5f7580daf503a4c05d4fc84b7fb9  PPHGNetV2_B1_stage1.pth' '41272985db6136ac11732b246c7ea794dcc203d0a8cbc463152c840b9d9f22d1  PPHGNetV2_B2_stage1.pth' '6be537deffd46d1c86e1a9316d79439cda9cebe9984fea91f486bdea4e716840  PPHGNetV2_B3_stage1.pth' 'a72ad8d32902c90f5fa07f642034955d0ed9149c46d4d97f0e2ec36344d24bea  PPHGNetV2_B4_stage1.pth' '812d5cde50e415abfb1ea1dd27121fa4f861522a327e48033a3cea8d604b3545  PPHGNetV2_B5_stage1.pth' '231b0078840733f4f9b91076e5fb9487a75a14eeaa0aa16a5b0e826cc6844731  PPHGNetV2_B6_stage1.pth' | sha256sum -c -

COPY _shared/detr_family/ /opt/recipe/shared/
RUN touch /opt/recipe/shared/__init__.py
WORKDIR /opt/recipe/src
