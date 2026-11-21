-- mlp:query SetEvaluationState :exec
UPDATE registry.evaluations SET state = @state, state_reason = @reason, deployment_id = coalesce(@deployment_id, deployment_id),
  started_at = CASE WHEN @state = 'running' AND started_at IS NULL THEN @now ELSE started_at END,
  finished_at = CASE WHEN @state IN ('succeeded', 'failed', 'cancelled') THEN @now ELSE finished_at END
WHERE id = @id AND state NOT IN ('succeeded', 'failed', 'cancelled');
