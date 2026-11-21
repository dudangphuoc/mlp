#!/bin/sh
# mlp-net (P0-16, D-26): WireGuard overlay 10.88.0.0/16 in a NET_ADMIN container; other containers share its network namespace
# (`--network container:mlp-net` / `network_mode: service:wireguard`). The private key lives in /state (volume) — it never leaves the container.
#   mlp-net keygen                  → create the key if missing, print the public key
#   mlp-net hub                     → 10.88.0.1/16, listen on UDP 51820, sync peers from /net/peers ("<pubkey> <ip>" per line) every 2 s,
#                                     write the hub's public key to /net/hub.pub
#   mlp-net spoke                   → WG_ADDR (10.88.x.y/16), PEER_PUB, PEER_ENDPOINT (host:51820), PEER_ALLOWED (10.88.0.0/16)
# Remote hub (k8s hostNetwork, e.g. cenos): IFACE=mlp0 (avoids clashing with the machine's wg0), PEERS_URL + PEERS_TOKEN (fetch peers from the api over the overlay),
#   STATIC_PEERS="<pub> <ip>;…" (control plane — present before the list can be fetched), ROUTE_IN=1 (lets pods/hosts outside the overlay reach 10.88/16: FORWARD + MASQUERADE).
# MTU (default 1380; 1300 when UDP is nested inside another WireGuard tunnel).
set -eu
IF=${IFACE:-wg0}; MTU=${MTU:-1380}
STATE=/state; mkdir -p "$STATE"; umask 077
[ -s "$STATE/private" ] || wg genkey > "$STATE/private"
PUB=$(wg pubkey < "$STATE/private")
case "${1:-}" in
  keygen) echo "$PUB" ;;
  hub)
    # /net is shared with the api (non-root user): only the public key + peer list — no secrets.
    mkdir -p /net; chmod 0777 /net; echo "$PUB" > /net/hub.pub; touch /net/peers; chmod 0644 /net/hub.pub; chmod 0666 /net/peers
    if ! ip link add "$IF" type wireguard 2>/dev/null; then mkdir -p /dev/net; [ -c /dev/net/tun ] || mknod /dev/net/tun c 10 200; wireguard-go "$IF"; fi
    wg set "$IF" private-key "$STATE/private" listen-port "${WG_PORT:-51820}"
    ip addr replace "${WG_ADDR:-10.88.0.1/16}" dev "$IF"; ip link set "$IF" mtu "$MTU" up
    if [ "${ROUTE_IN:-0}" = 1 ]; then
      for r in "FORWARD -i $IF -j ACCEPT" "FORWARD -o $IF -j ACCEPT"; do iptables -C $r 2>/dev/null || iptables -I $r; done
      r="POSTROUTING -o $IF ! -s 10.88.0.0/16 -j MASQUERADE"; iptables -t nat -C $r 2>/dev/null || iptables -t nat -I $r
    fi
    echo "mlp-net hub ${WG_ADDR:-10.88.0.1/16} :${WG_PORT:-51820} if=$IF pub=$PUB"
    last=""
    while true; do
      if [ -n "${PEERS_URL:-}" ]; then
        wget -q -T 5 -O /net/peers.new --header "Authorization: Bearer ${PEERS_TOKEN:-}" "$PEERS_URL" 2>/dev/null && mv /net/peers.new /net/peers || rm -f /net/peers.new
      fi
      cur=$( { cat /net/peers 2>/dev/null; echo "${STATIC_PEERS:-}" | tr ';' '\n'; } | grep . | sort -u)
      if [ "$cur" != "$last" ]; then
        want=$(echo "$cur" | awk 'NF==2 {print $1}')
        for p in $(wg show "$IF" peers); do echo "$want" | grep -qxF "$p" || wg set "$IF" peer "$p" remove; done
        echo "$cur" | while read -r key ip; do [ -n "$key" ] && wg set "$IF" peer "$key" allowed-ips "$ip/32"; done
        echo "mlp-net: $(echo "$cur" | grep -c . || true) peer"; last="$cur"
      fi
      sleep 2
    done ;;
  spoke)
    : "${WG_ADDR:?} ${PEER_PUB:?} ${PEER_ENDPOINT:?}"
    if ! ip link add "$IF" type wireguard 2>/dev/null; then mkdir -p /dev/net; [ -c /dev/net/tun ] || mknod /dev/net/tun c 10 200; wireguard-go "$IF"; fi
    wg set "$IF" private-key "$STATE/private" peer "$PEER_PUB" endpoint "$PEER_ENDPOINT" allowed-ips "${PEER_ALLOWED:-10.88.0.0/16}" persistent-keepalive 25
    ip addr replace "$WG_ADDR" dev "$IF"; ip link set "$IF" mtu "$MTU" up
    echo "mlp-net spoke $WG_ADDR → $PEER_ENDPOINT pub=$PUB"
    # The tunnel is up once the hub answers; print the handshake state every minute so it shows in `docker logs`.
    while true; do sleep 60; echo "handshake: $(wg show "$IF" latest-handshakes | awk '{print $2}')"; done ;;
  check)   # runs in the spoke's network namespace: is the tunnel to the hub up? (called by the control plane after bringing up the spoke)
    for i in 1 2 3 4 5 6 7 8 9 10; do ping -c 1 -W 2 "${HUB_IP:-10.88.0.1}" >/dev/null 2>&1 && { echo ok; exit 0; }; sleep 2; done
    echo "cannot ping hub ${HUB_IP:-10.88.0.1} — is UDP to $(wg show "$IF" endpoints 2>/dev/null | awk '{print $2}') blocked?"; exit 1 ;;
  *) echo "Usage: mlp-net keygen|hub|spoke|check" >&2; exit 2 ;;
esac
