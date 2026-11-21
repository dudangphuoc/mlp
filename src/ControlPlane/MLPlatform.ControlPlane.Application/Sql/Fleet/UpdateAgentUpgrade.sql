-- mlp:query UpdateAgentUpgrade :exec
-- mlp:no-workspace internal orchestration loop updates by job id (created within a workspace)
UPDATE fleet.agent_upgrades SET state = @state, phase = @phase, message = @message, updated_at = @now,
       finished_at = CASE WHEN @state IN ('succeeded', 'failed', 'skipped') THEN @now ELSE finished_at END
WHERE id = @id;
