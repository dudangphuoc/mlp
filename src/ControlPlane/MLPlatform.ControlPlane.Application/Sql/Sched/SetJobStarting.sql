-- mlp:query SetJobStarting :exec
-- A new attempt starts reserving resources; a "Waiting for …" reason from the previous try stays visible until resources arrive.
UPDATE sched.jobs SET state = 'starting', state_reason = CASE WHEN state_reason LIKE 'Waiting for %' THEN state_reason ELSE NULL END
WHERE id = @id AND state <> 'cancelled';
