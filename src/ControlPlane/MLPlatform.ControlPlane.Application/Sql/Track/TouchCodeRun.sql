-- mlp:query TouchCodeRun :exec
UPDATE track.runs SET heartbeat_at = @now WHERE id = @id AND workspace_id = @workspace_id AND origin = 'code' AND state = 'running';
