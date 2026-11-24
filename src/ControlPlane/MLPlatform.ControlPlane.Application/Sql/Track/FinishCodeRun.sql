-- mlp:query FinishCodeRun :exec
UPDATE track.runs SET state = @state, exit_code = @exit_code, ended_at = @now, heartbeat_at = @now
WHERE id = @id AND workspace_id = @workspace_id AND origin = 'code' AND state = 'running';
