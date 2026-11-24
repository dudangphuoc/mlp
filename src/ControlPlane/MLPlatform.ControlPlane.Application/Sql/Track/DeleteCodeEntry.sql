-- mlp:query DeleteCodeEntry :exec
DELETE FROM track.code_entries WHERE id = @id AND workspace_id = @workspace_id;
