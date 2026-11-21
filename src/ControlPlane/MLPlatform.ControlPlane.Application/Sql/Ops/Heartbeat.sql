-- mlp:query AttemptHeartbeat :exec
UPDATE sched.job_attempts SET last_heartbeat_at = @now WHERE id = @id;
