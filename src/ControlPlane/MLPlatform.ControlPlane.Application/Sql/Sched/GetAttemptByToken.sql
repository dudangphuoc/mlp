-- mlp:query GetAttemptByToken :one
SELECT a.id, a.job_id, a.number, a.host_ids, a.state, j.spec::text AS spec, j.workspace_id
FROM sched.job_attempts a JOIN sched.jobs j ON j.id = a.job_id
WHERE a.token_hash = @token_hash;
