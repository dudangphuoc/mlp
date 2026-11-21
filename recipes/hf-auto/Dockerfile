# hf-auto — build context: the recipes/ directory
FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
# Versions match the base image's torch 2.5.1: transformers 5.x needs torch >= 2.6 (torch.accelerator) and crashed at load;
# torchaudio pinned so pip does not pull another torch; scipy: OWLv2 image processor.
RUN pip install --no-cache-dir transformers==4.48.3 "accelerate<1.4" "timm<1.1" torchaudio==2.5.1 pillow scipy
WORKDIR /opt/recipe
COPY hf-auto/handler.py ./
