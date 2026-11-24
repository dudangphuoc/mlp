# tabular-gbdt — CPU by default; XGBoost uses CUDA when a GPU is granted.
# Base pinned by digest at build time (mlp build_recipe resolves and records it).
FROM python:3.12-slim

COPY tabular-gbdt/constraints.txt /opt/recipe/constraints.txt
ENV PIP_CONSTRAINT=/opt/recipe/constraints.txt
RUN pip install --no-cache-dir scikit-learn==1.9.1 xgboost==3.4.1 lightgbm==4.7.0 pandas pyarrow joblib \
    && apt-get update && apt-get install -y --no-install-recommends libgomp1 && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/recipe
COPY tabular-gbdt/train.py tabular-gbdt/handler.py ./
# The platform appends a final layer with mlp-shim, mlplatform-sdk and mlp-serve (20/08 §5).
