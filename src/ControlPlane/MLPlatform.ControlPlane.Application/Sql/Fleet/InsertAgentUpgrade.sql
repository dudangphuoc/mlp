-- mlp:query InsertAgentUpgrade :exec
INSERT INTO fleet.agent_upgrades (id, workspace_id, host_id, from_version, to_version, state, phase, message, created_by, created_at, updated_at, finished_at)
VALUES (@id, @workspace_id, @host_id, @from_version, @to_version, @state, '', @message, @created_by, @now, @now, CASE WHEN @state = 'skipped' THEN @now END)
ON CONFLICT DO NOTHING;
