-- mlp:query FinishEnvironment :exec
UPDATE catalog.environments SET state = @state, error = @error, built_at = @built_at
WHERE workspace_id = @workspace_id AND env_tag = @env_tag AND host_id = @host_id;
