-- mlp:query SuspectOfJob :one
SELECT a.id, a.job_id, j.workspace_id FROM sched.job_attempts a JOIN sched.jobs j ON j.id = a.job_id
WHERE a.job_id = @job_id AND j.workspace_id = @workspace_id AND a.state = 'suspect' LIMIT 1;
