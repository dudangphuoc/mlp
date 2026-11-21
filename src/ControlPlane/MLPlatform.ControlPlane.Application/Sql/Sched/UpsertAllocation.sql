-- mlp:query UpsertAllocation :exec
INSERT INTO sched.attempt_allocations (attempt_id, host_id, gpu_uuids, node_ip, reported_at)
VALUES (@attempt_id, @host_id, @gpu_uuids, @node_ip, @now)
ON CONFLICT (attempt_id, host_id) DO UPDATE SET gpu_uuids = excluded.gpu_uuids, node_ip = excluded.node_ip, reported_at = excluded.reported_at;
