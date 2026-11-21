-- mlp:query SetJobState :exec
UPDATE sched.jobs SET state = @state, state_reason = @reason,
       started_at = CASE WHEN @state = 'running' AND started_at IS NULL THEN @now ELSE started_at END,
       finished_at = CASE WHEN @state IN ('succeeded','failed','cancelled') THEN @now ELSE finished_at END
WHERE id = @id;
