-- mlp:query SetServerState :exec
UPDATE nb.servers SET state = @state, state_reason = @reason,
  state_at = CASE WHEN state IS DISTINCT FROM @state THEN @now ELSE state_at END,
  host_id = coalesce(@host_id, host_id), endpoint = CASE WHEN @state IN ('stopped', 'failed') THEN NULL ELSE coalesce(@endpoint, endpoint) END,
  started_at = CASE WHEN @state = 'running' THEN @now ELSE started_at END,
  stopped_at = CASE WHEN @state IN ('stopped', 'failed') THEN @now ELSE stopped_at END,
  last_activity_at = CASE WHEN @state = 'running' THEN @now ELSE last_activity_at END
WHERE id = @id;
