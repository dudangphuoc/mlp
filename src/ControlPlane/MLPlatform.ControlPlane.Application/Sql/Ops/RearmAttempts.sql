-- mlp:query RearmAttempts :exec
-- Control plane just started (after a deploy, a crash or a long outage): machines kept running on their own meanwhile, so every
-- watchdog clock restarts from now. Running attempts wait (suspect) for their machine's agent to reconnect and report its containers.
UPDATE sched.job_attempts SET state = 'suspect', suspect_until = @until, last_heartbeat_at = @now, signal_lost_at = NULL
WHERE state IN ('launching', 'running');
UPDATE sched.job_attempts SET suspect_until = @until WHERE state = 'suspect' AND suspect_until < @until;
UPDATE sched.jobs SET progress_changed_at = @now, stalled_at = NULL WHERE state = 'running' AND progress_changed_at IS NOT NULL;
