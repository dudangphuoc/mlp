-- mlp:query SetProgress :exec
UPDATE sched.jobs SET progress = @progress::jsonb, progress_at = @now WHERE id = @id;
