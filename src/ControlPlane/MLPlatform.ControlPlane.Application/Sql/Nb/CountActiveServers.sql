-- mlp:query CountActiveServers :scalar
SELECT count(*) FROM nb.servers WHERE workspace_id = @workspace_id AND user_id = @user_id AND state IN ('queued', 'starting', 'running');
