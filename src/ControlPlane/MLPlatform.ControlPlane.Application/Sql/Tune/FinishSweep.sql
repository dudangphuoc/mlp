-- mlp:query FinishSweep :exec
UPDATE tune.sweeps SET state = @state, state_reason = @reason, finished_at = @now WHERE id = @id AND state = 'running';
