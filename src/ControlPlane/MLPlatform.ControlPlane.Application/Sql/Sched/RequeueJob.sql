-- mlp:query RequeueJob :exec
UPDATE sched.jobs SET state = 'queued', state_reason = @reason, attempt_count = attempt_count - 1,
       not_before = now() + interval '5 seconds'
WHERE id = @id AND state <> 'cancelled';   -- a cancelled job never returns to the queue
