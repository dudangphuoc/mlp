# d-fine — build context: the recipes/ directory  (docker build -f d-fine/Dockerfile recipes/)
# torch 2.5.1: avoids the torch.load(weights_only=True) default change in 2.6 (checkpoints contain optimizer/EMA).
FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime

RUN apt-get update && apt-get install -y --no-install-recommends git curl libgl1 libglib2.0-0 && rm -rf /var/lib/apt/lists/*
ARG DFINE_COMMIT=956d1709314c2c6a4df6f34de232054578a7449f
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
    && for b in 0 1 2 3 4 5 6; do curl -fsSL -O https://github.com/Peterande/storage/releases/download/dfinev1.0/PPHGNetV2_B${b}_stage1.pth; done

COPY _shared/detr_family/ /opt/recipe/shared/
RUN touch /opt/recipe/shared/__init__.py
WORKDIR /opt/recipe/src
