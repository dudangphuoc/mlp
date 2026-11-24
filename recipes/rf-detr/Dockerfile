# rf-detr — rfdetr 1.12.1 pip package (requires-python >=3.10, torch>=2.2).
FROM pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime

COPY rf-detr/constraints.txt /opt/recipe/constraints.txt
ENV PIP_CONSTRAINT=/opt/recipe/constraints.txt
RUN pip install --no-cache-dir "rfdetr[train,onnx]==1.12.1" tensorboard
WORKDIR /opt/recipe
COPY rf-detr/run_rfdetr.py rf-detr/handler.py ./
