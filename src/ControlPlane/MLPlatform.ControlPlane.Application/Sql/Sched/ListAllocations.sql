-- mlp:query ListAllocations :many
SELECT host_id, gpu_uuids, node_ip FROM sched.attempt_allocations WHERE attempt_id = @attempt_id ORDER BY host_id;
