# sam2 — build context: the recipes/ directory
FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime

RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*

ARG SAM2_COMMIT=2b90b9f5ceec907a1c18123530e92e794ad901a4
# SAM2_BUILD_CUDA=0: do not compile the CUDA extension (no nvcc needed in the runtime image)
COPY sam2/constraints.txt /opt/recipe/constraints.txt
ENV PIP_CONSTRAINT=/opt/recipe/constraints.txt
RUN git clone https://github.com/facebookresearch/sam2.git /opt/recipe/src \
    && cd /opt/recipe/src && git checkout ${SAM2_COMMIT} \
    && SAM2_BUILD_CUDA=0 pip install --no-cache-dir --no-build-isolation -e . opencv-python-headless

WORKDIR /opt/recipe
COPY sam2/handler.py ./
