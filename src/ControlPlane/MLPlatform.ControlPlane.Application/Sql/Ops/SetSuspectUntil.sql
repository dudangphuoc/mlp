-- mlp:query SetSuspectUntil :exec
UPDATE sched.job_attempts SET suspect_until = @until WHERE id = @id AND state = 'suspect';
