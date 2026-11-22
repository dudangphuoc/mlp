-- mlp:query HostGpuAllocations :many
-- mlp:no-workspace host already resolved in its workspace by the caller
SELECT al.attempt_id, a.job_id, j.name AS job_name, al.gpu_uuids, j.spec::text AS spec
FROM sched.attempt_allocations al
JOIN sched.job_attempts a ON a.id = al.attempt_id
JOIN sched.jobs j ON j.id = a.job_id
WHERE al.host_id = @host_id AND a.state IN ('launching', 'running', 'suspect') AND cardinality(al.gpu_uuids) > 0;
