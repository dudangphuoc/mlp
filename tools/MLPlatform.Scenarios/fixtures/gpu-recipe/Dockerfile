# GPU recipe image for testing real machines (build on the GPU machine itself: docker build -t mlp-test/gpu-recipe:dev .)
FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime
COPY mlp-shim /usr/local/bin/mlp-shim
COPY train_gpu.py /opt/recipe/src/train_gpu.py
RUN chmod +x /usr/local/bin/mlp-shim && mkdir -p /mnt/output /mnt/data /mnt/model && chmod 777 /mnt/output /mnt/data /mnt/model
