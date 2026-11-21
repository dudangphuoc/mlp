-- mlp:query RenameRun :exec
UPDATE track.runs SET name = @name WHERE id = @id AND workspace_id = @workspace_id;
