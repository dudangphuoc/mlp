-- mlp:query RetryJob :exec
UPDATE sched.jobs SET state = 'queued', state_reason = @reason, queued_at = @now WHERE id = @id;
