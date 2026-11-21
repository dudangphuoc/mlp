-- mlp:query ListOverlayPeers :many
-- mlp:no-workspace the instance-wide WireGuard hub needs every machine with a key (not per workspace)
-- A key belongs to one machine only: reinstalling on the same machine (key kept in a volume) → take the most recently seen record.
SELECT DISTINCT ON (wg_public_key) wg_public_key, host(overlay_ip) AS overlay_ip FROM fleet.hosts
WHERE wg_public_key IS NOT NULL AND wg_public_key <> '' AND disconnected_at IS NULL
ORDER BY wg_public_key, last_seen_at DESC NULLS LAST;
