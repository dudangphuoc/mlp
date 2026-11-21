-- mlp:query SignalLost :many
-- Shim silent past the threshold while the machine is still online (tier 2 of 20/10 §2.3) — only jobs with a shim (that have reported progress before).
SELECT a.id, a.job_id, j.workspace_id, j.owner_id, j.name, a.signal_lost_at
FROM sched.job_attempts a JOIN sched.jobs j ON j.id = a.job_id JOIN fleet.hosts h ON h.id = a.host_ids[1]
WHERE a.state = 'running' AND j.progress IS NOT NULL AND h.state <> 'offline'
  AND coalesce(a.last_heartbeat_at, a.started_at) < @before;
