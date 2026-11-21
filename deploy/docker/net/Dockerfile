# mlp-net — WireGuard overlay (P0-16). The agent rebuilds exactly this on the machine (embedded Dockerfile) if the image is missing.
FROM alpine:3.22
RUN apk add --no-cache wireguard-tools iproute2 wireguard-go iptables
COPY mlp-net.sh /usr/local/bin/mlp-net
RUN chmod +x /usr/local/bin/mlp-net
ENTRYPOINT ["mlp-net"]
