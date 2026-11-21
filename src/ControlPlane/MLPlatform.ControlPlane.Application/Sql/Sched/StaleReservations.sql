-- mlp:query StaleReservations :many
-- mlp:no-workspace instance-wide recovery loop
SELECT id, job_id FROM sched.job_attempts WHERE state = 'reserving' AND started_at < @before;
