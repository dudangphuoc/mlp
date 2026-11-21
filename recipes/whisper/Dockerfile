# whisper — fine-tune with the transformers seq2seq script, pinned to tag v5.19.0.
FROM pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime

RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg curl && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir "transformers==5.19.0" "evaluate==0.4.6" "jiwer==4.0.0" \
        librosa accelerate tensorboard "datasets[audio]" soundfile

WORKDIR /opt/recipe
# The script was removed from main (PR #49357); fetched from tag v5.19.0
RUN curl -fsSL -o run_speech_recognition_seq2seq.py \
    https://raw.githubusercontent.com/huggingface/transformers/v5.19.0/examples/pytorch/speech-recognition/run_speech_recognition_seq2seq.py
COPY whisper/handler.py ./
# The final layer (mlp-shim, mlplatform-sdk, mlp-serve) is added by the platform at build time.
