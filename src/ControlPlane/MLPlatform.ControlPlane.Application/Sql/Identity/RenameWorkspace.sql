-- mlp:query RenameWorkspace :exec
UPDATE identity.workspaces SET name = @name WHERE id = @workspace_id;
