-- mlp:query InsertAttempt :exec
INSERT INTO sched.job_attempts (id, job_id, number, token_hash, host_ids, bundles, state, started_at)
VALUES (@id, @job_id, @number, @token_hash, @host_ids, @bundles::jsonb, 'reserving', @now);
