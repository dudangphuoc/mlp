-- mlp:query ExpiredSuspects :many
SELECT a.id, a.job_id, j.workspace_id FROM sched.job_attempts a JOIN sched.jobs j ON j.id = a.job_id
WHERE a.state = 'suspect' AND a.suspect_until <= @now;
