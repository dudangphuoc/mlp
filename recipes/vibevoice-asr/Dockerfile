# vibevoice-asr — per the official guide: NVIDIA PyTorch container (24.07–25.12 tested), pip install -e ., peft, ffmpeg.
FROM nvcr.io/nvidia/pytorch:25.12-py3

RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg git && rm -rf /var/lib/apt/lists/*

ARG VIBEVOICE_COMMIT=16fb2cb1217c9934a886e1948ffb06120caa2df5
RUN git clone https://github.com/microsoft/VibeVoice.git /opt/recipe/src \
    && cd /opt/recipe/src && git checkout ${VIBEVOICE_COMMIT} \
    && pip install --no-cache-dir -e . peft tensorboard jiwer
# flash-attn: install only if the base image lacks it (official docs)
RUN python -c "import flash_attn" 2>/dev/null || pip install --no-cache-dir flash-attn --no-build-isolation

WORKDIR /opt/recipe
COPY vibevoice-asr/handler.py ./
COPY _shared/converters/ /opt/recipe/shared/converters/
# eval_wer.py: see openItems in mlp-recipe.yaml
