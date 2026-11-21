-- mlp:query ActiveJobsForUser :many
-- "Running" tray (S-00-3): my jobs that are running + finished within the last 60 s; includes the warning flags of the latest attempt.
SELECT j.id, j.name, j.kind, j.state, j.state_reason, j.progress::text AS progress, j.stalled_at,
  la.state AS attempt_state, la.signal_lost_at, la.suspect_until
FROM sched.jobs j
LEFT JOIN LATERAL (SELECT state, signal_lost_at, suspect_until FROM sched.job_attempts WHERE job_id = j.id ORDER BY number DESC LIMIT 1) la ON true
WHERE j.workspace_id = @workspace_id AND j.owner_id = @owner_id
  AND (j.state IN ('queued', 'scheduling', 'starting', 'running') OR j.finished_at > @recent)
ORDER BY j.created_at DESC LIMIT 50;
