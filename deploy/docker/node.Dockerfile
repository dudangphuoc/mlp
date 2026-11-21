# mlp-node — thin Ray worker (P0-9): same Ray/Python versions as the head, with the mlp_holder/mlp_serve packages.
ARG RAY_IMAGE=rayproject/ray:2.60.0-py312-cpu
FROM ${RAY_IMAGE}
COPY python/ /opt/mlp/
ENV PYTHONPATH=/opt/mlp
LABEL io.mlplatform.image=mlp-node
