-- mlp:query ProgressChanged :exec
UPDATE sched.jobs SET progress_changed_at = @now, stalled_at = NULL WHERE id = @id;
