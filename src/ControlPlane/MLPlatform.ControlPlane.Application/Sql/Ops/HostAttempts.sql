-- mlp:query HostAttempts :many
SELECT a.id, a.job_id, a.state, j.workspace_id, a.host_ids FROM sched.job_attempts a JOIN sched.jobs j ON j.id = a.job_id
WHERE @host_id = ANY(a.host_ids) AND (a.state IN ('launching', 'running', 'suspect') OR a.id = ANY(@ids));
