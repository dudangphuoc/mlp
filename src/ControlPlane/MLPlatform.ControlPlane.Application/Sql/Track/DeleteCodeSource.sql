-- mlp:query DeleteCodeSource :exec
DELETE FROM track.code_sources WHERE id = @id AND workspace_id = @workspace_id;
