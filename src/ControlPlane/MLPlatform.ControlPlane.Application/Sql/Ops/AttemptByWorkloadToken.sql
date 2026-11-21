-- mlp:query AttemptByWorkloadToken :one
SELECT a.id, a.job_id, a.state, j.workspace_id, a.host_ids FROM sched.job_attempts a JOIN sched.jobs j ON j.id = a.job_id
WHERE a.workload_token_hash = @token_hash;
