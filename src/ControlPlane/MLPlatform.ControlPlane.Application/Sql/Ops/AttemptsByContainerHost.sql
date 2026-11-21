-- mlp:query AttemptGpus :many
SELECT al.attempt_id, al.gpu_uuids FROM sched.attempt_allocations al JOIN sched.job_attempts a ON a.id = al.attempt_id
WHERE al.host_id = @host_id AND a.state IN ('launching', 'running', 'suspect');
