# rt-detrv4 — build context: recipes/  (docker build -f rt-detrv4/Dockerfile recipes/)
FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime

RUN apt-get update && apt-get install -y --no-install-recommends git curl libgl1 libglib2.0-0 && rm -rf /var/lib/apt/lists/*
ARG RTV4_COMMIT=55fefaaed7efe2a5f72d0a18fd4e05965e35c292
ARG DINOV3_COMMIT=6876159a11b4df116f30f667f8c9888617df0751
RUN git clone https://github.com/AIMIA-PKU/RT-DETRv4.git /opt/recipe/src \
    && cd /opt/recipe/src && git checkout ${RTV4_COMMIT} \
    && pip install --no-cache-dir -r requirements.txt \
    && pip install --no-cache-dir opencv-python-headless onnx onnxsim gdown
# DINOv3 code for the teacher (weights NOT included: an Admin uploads them to the Model Zoo after Meta approval)
RUN git clone https://github.com/facebookresearch/dinov3.git /opt/recipe/dinov3 \
    && cd /opt/recipe/dinov3 && git checkout ${DINOV3_COMMIT} \
    && pip install --no-cache-dir -r requirements.txt
# Pre-download the HGNetv2 backbone into ./pretrain/hgnetv2/ (avoids the get_rank() branch when downloading during training)
RUN mkdir -p /opt/recipe/src/pretrain/hgnetv2 && cd /opt/recipe/src/pretrain/hgnetv2 \
    && for b in 0 1 2 3 4 5 6; do curl -fsSL -O https://github.com/Peterande/storage/releases/download/dfinev1.0/PPHGNetV2_B${b}_stage1.pth; done

COPY _shared/detr_family/ /opt/recipe/shared/
RUN touch /opt/recipe/shared/__init__.py
WORKDIR /opt/recipe/src
