-- mlp:query HostAttemptJobs :many
SELECT a.id AS attempt_id, j.id AS job_id, j.name AS job_name, al.host_id, al.gpu_uuids, j.spec::text AS spec
FROM sched.job_attempts a JOIN sched.jobs j ON j.id = a.job_id JOIN sched.attempt_allocations al ON al.attempt_id = a.id
WHERE j.workspace_id = @workspace_id AND a.state IN ('launching', 'running', 'suspect');
