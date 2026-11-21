-- mlp:query SetHostOnline :exec
-- Docker not running on the machine → keep "degraded" with a reason (heartbeat must not overwrite it to online).
UPDATE fleet.hosts SET state = CASE WHEN docker_ok THEN @state ELSE 'degraded' END,
       state_reason = CASE WHEN docker_ok THEN NULL ELSE 'Docker is not running on this machine: ' || coalesce(docker_error, '') END,
       agent_version = @agent_version, host_info = coalesce(@host_info, host_info), last_seen_at = @now
WHERE id = @id;
