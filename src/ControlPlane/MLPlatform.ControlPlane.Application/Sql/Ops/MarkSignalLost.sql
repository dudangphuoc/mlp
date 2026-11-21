-- mlp:query MarkSignalLost :exec
UPDATE sched.job_attempts SET signal_lost_at = @now WHERE id = @id AND signal_lost_at IS NULL;
