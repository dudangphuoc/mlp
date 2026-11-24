# generic-onnx — build context: the recipes/ directory
FROM nvidia/cuda:12.6.3-cudnn-runtime-ubuntu24.04
# system Python (PEP 668): also needed by packages that run pip themselves during install (tensorrt-cu12)
ENV PIP_BREAK_SYSTEM_PACKAGES=1
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-pip && rm -rf /var/lib/apt/lists/*
# onnxruntime-gpu 1.22 = CUDA 12 + cuDNN 9 (matches the base image); newer wheels need CUDA 13 libs and silently fall back to CPU
# onnx + psutil: the optimize job (quantization, FP16, benchmark) runs in this image too — see Registry/Optimizations.cs
COPY generic-onnx/constraints.txt /opt/recipe/constraints.txt
ENV PIP_CONSTRAINT=/opt/recipe/constraints.txt
RUN pip3 install --no-cache-dir --break-system-packages onnxruntime-gpu==1.22.0 onnx==1.18.0 numpy pillow psutil==7.0.0
# conversions from ONNX (Registry/Exports.cs DerivedScript): OpenVINO IR, TensorRT engines (CUDA 12 build, built on the target GPU)
RUN pip3 install --no-cache-dir --break-system-packages openvino==2025.0.0 tensorrt-cu12==10.7.0
WORKDIR /opt/recipe
COPY generic-onnx/handler.py generic-onnx/inspect_onnx.py ./
