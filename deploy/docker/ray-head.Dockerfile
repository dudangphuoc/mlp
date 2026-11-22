# mlp-ray-head — trimmed Ray head with the placement holder code baked in (no host mount; k3s and compose alike).
FROM rayproject/ray:2.60.0-py312-cpu
COPY python /opt/mlp
CMD ["bash", "/opt/mlp/mlp_holder/ray_head.sh"]
