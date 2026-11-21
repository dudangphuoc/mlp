-- mlp:query ClearSignalLost :exec
UPDATE sched.job_attempts SET signal_lost_at = NULL WHERE id = @id;
