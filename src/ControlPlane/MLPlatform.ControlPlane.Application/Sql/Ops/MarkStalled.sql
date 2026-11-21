-- mlp:query MarkStalled :exec
UPDATE sched.jobs SET stalled_at = @now WHERE id = @id;
