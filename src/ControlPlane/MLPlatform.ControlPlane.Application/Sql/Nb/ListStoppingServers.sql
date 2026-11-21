-- mlp:query ListStoppingServers :many
-- mlp:no-workspace background reaper reconciles every "stopping" notebook with its job
SELECT s.id, s.job_id, coalesce(j.state, '') AS job_state, coalesce(s.state_at, s.created_at) AS state_at, s.state_reason
FROM nb.servers s LEFT JOIN sched.jobs j ON j.id = s.job_id WHERE s.state = 'stopping';
