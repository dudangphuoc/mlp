-- mlp:query MarkSuspect :many
-- Machine lost its agent channel → every attempt running on it waits for the machine to come back (20/10 §4.4).
UPDATE sched.job_attempts a SET state = 'suspect', suspect_until = @until
FROM sched.jobs j
WHERE j.id = a.job_id AND a.state IN ('launching', 'running') AND @host_id = ANY(a.host_ids)
RETURNING a.id, a.job_id, j.workspace_id;
