-- mlp:query ListJobs :many
SELECT j.id, j.workspace_id, j.kind, j.name, j.state, j.state_reason, j.attempt_count, j.created_at, j.started_at, j.finished_at,
       (SELECT a.exit_reason FROM sched.job_attempts a WHERE a.job_id = j.id ORDER BY a.number DESC LIMIT 1) AS last_exit_reason
FROM sched.jobs j WHERE j.workspace_id = @workspace_id ORDER BY j.created_at DESC LIMIT 200;
