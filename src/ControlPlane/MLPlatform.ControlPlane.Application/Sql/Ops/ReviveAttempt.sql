-- mlp:query ReviveAttempt :exec
UPDATE sched.job_attempts SET state = 'running', suspect_until = NULL WHERE id = @id AND state = 'suspect';
