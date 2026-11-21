-- mlp:query ResumeJob :exec
-- Manually resume a failed job: requeue it with one extra attempt (checkpoint kept).
UPDATE sched.jobs SET state = 'queued', state_reason = @reason, queued_at = @now, finished_at = NULL,
  max_attempts = greatest(max_attempts, attempt_count + 1), not_before = NULL
WHERE id = @id AND workspace_id = @workspace_id AND state = 'failed';
