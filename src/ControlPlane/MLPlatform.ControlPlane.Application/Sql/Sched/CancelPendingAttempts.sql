-- mlp:query CancelPendingAttempts :exec
-- A cancelled job's attempts that have no container yet stop here; a running attempt is finalized when its container exits.
UPDATE sched.job_attempts a SET state = 'cancelled', exit_reason = 'cancelled', finished_at = @now
FROM sched.jobs j
WHERE j.id = a.job_id AND j.workspace_id = @workspace_id AND a.job_id = @job_id AND a.state IN ('reserving', 'launching');
