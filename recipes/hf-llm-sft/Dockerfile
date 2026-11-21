# hf-llm-sft — TRL 1.18.1 (Python >=3.11, torch>=2.6).
FROM pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime

RUN pip install --no-cache-dir "trl[peft]==1.18.1" "transformers==5.23.0" "peft==0.21.3" \
        "accelerate==1.16.0" "datasets==5.2.0" bitsandbytes tensorboard
WORKDIR /opt/recipe
COPY hf-llm-sft/handler.py ./
