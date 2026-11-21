-- mlp:query JobAttempts :many
SELECT a.id, a.number, a.host_ids, a.state, a.exit_code, a.exit_reason, a.message, a.started_at, a.finished_at, a.last_heartbeat_at, a.suspect_until, a.resumed_from
FROM sched.job_attempts a WHERE a.job_id = @job_id ORDER BY a.number;
