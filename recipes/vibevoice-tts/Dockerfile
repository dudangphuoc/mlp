# vibevoice-tts — community fork (MIT). The fork pins transformers==4.51.3, accelerate==1.6.0, datasets==3.5.0.
FROM pytorch/pytorch:2.6.0-cuda12.4-cudnn9-devel

RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg git && rm -rf /var/lib/apt/lists/*

ARG FORK_COMMIT=952326ddb264062466a888cf32a5b2f4e803e16e
COPY vibevoice-tts/constraints.txt /opt/recipe/constraints.txt
ENV PIP_CONSTRAINT=/opt/recipe/constraints.txt
RUN git clone https://github.com/vibevoice-community/VibeVoice.git /opt/recipe/src \
    && cd /opt/recipe/src && git checkout ${FORK_COMMIT} \
    && pip install --no-cache-dir -e . tensorboard
ENV MAX_JOBS=4
RUN python -c "import flash_attn" 2>/dev/null || pip install --no-cache-dir "flash-attn==2.7.4.post1" --no-build-isolation || true
# flash-attn is optional: the code falls back to SDPA without it (quality may be lower)

WORKDIR /opt/recipe
COPY vibevoice-tts/handler.py ./
COPY _shared/converters/ /opt/recipe/shared/converters/
