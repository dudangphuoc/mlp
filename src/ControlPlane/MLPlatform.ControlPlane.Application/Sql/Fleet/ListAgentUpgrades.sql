-- mlp:query ListAgentUpgrades :many
SELECT u.id, u.host_id, h.name AS host_name, u.from_version, u.to_version, u.state, u.phase, u.message, u.created_at, u.finished_at
FROM fleet.agent_upgrades u JOIN fleet.hosts h ON h.id = u.host_id
WHERE u.workspace_id = @workspace_id AND (@host_id::uuid IS NULL OR u.host_id = @host_id)
ORDER BY u.created_at DESC LIMIT 100;
