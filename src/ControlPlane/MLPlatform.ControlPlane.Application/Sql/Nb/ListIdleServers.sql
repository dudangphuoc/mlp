-- mlp:query ListIdleServers :many
SELECT s.id, s.workspace_id, s.user_id, u.username, s.name, s.profile, s.image, s.ide, s.cpus, s.memory_gb, s.gpus, s.host_id, s.endpoint, s.job_id, s.state, s.state_reason, s.idle_timeout_min, s.last_activity_at, s.created_at, s.started_at, s.stopped_at FROM nb.servers s JOIN identity.users u ON u.id = s.user_id
WHERE s.state = 'running' AND s.last_activity_at + make_interval(mins => s.idle_timeout_min) < @now;
