-- mlp:query DeleteServer :exec
DELETE FROM nb.servers WHERE id = @id AND workspace_id = @workspace_id;
