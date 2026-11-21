# hf-text — transformers example scripts pinned to tag v5.19.0 (removed from main by PR #49357).
FROM pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir "transformers==5.19.0" datasets "evaluate==0.4.6" seqeval accelerate tensorboard scikit-learn
WORKDIR /opt/recipe
ARG TF_TAG=v5.19.0
RUN curl -fsSL -o run_classification.py https://raw.githubusercontent.com/huggingface/transformers/${TF_TAG}/examples/pytorch/text-classification/run_classification.py \
 && curl -fsSL -o run_ner.py https://raw.githubusercontent.com/huggingface/transformers/${TF_TAG}/examples/pytorch/token-classification/run_ner.py
COPY hf-text/handler.py ./
