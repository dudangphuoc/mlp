-- mlp:query GpuJobMarks :many
-- mlp:no-workspace host already resolved in its workspace by the caller
SELECT DISTINCT ON (a.id) a.job_id, j.name AS job_name, a.started_at, a.finished_at
FROM sched.attempt_allocations al
JOIN sched.job_attempts a ON a.id = al.attempt_id
JOIN sched.jobs j ON j.id = a.job_id
WHERE al.host_id = @host_id AND @gpu_uuid = ANY(al.gpu_uuids) AND coalesce(a.finished_at, now()) >= @since
  AND (a.finished_at IS NOT NULL OR a.state IN ('launching', 'running', 'suspect'))
ORDER BY a.id LIMIT 200;
