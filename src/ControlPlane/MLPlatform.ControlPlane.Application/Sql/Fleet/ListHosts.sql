-- mlp:query ListHosts :many
SELECT id, workspace_id, name, state, state_reason, overlay_ip::text AS overlay_ip, agent_version, host_info, last_seen_at, disconnected_at
FROM fleet.hosts WHERE workspace_id = @workspace_id AND disconnected_at IS NULL ORDER BY name;
