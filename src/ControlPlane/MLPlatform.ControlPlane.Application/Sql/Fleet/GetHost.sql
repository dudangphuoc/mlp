-- mlp:query GetHost :one
SELECT id, workspace_id, name, state, state_reason, overlay_ip::text AS overlay_ip, agent_version, host_info, last_seen_at, disconnected_at
FROM fleet.hosts WHERE id = @id AND workspace_id = @workspace_id;
