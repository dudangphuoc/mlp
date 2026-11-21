-- mlp:query SetAttemptState :exec
UPDATE sched.job_attempts SET state = @state, exit_code = coalesce(@exit_code, exit_code),
       exit_reason = coalesce(@exit_reason, exit_reason), message = coalesce(@message, message),
       finished_at = CASE WHEN @state IN ('succeeded','failed','stopped') THEN @now ELSE finished_at END
WHERE id = @id;
