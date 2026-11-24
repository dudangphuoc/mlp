# ultralytics-yolo — AGPL-3.0 license (the recipe requires acknowledgment on first use).
FROM pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime

RUN apt-get update && apt-get install -y --no-install-recommends libgl1 libglib2.0-0 && rm -rf /var/lib/apt/lists/*
COPY ultralytics-yolo/constraints.txt /opt/recipe/constraints.txt
ENV PIP_CONSTRAINT=/opt/recipe/constraints.txt
RUN pip install --no-cache-dir "ultralytics==8.4.215" onnx onnxslim
# Disable usage data collection and automatic updates inside the container
RUN yolo settings sync=False
WORKDIR /opt/recipe
COPY ultralytics-yolo/handler.py ./
