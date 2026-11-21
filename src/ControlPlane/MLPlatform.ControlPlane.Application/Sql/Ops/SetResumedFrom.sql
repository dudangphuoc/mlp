-- mlp:query SetResumedFrom :exec
UPDATE sched.job_attempts SET resumed_from = @resumed_from WHERE id = @id;
