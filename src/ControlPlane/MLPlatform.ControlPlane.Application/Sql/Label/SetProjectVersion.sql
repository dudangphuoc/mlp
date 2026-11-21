-- mlp:query SetProjectVersion :exec
UPDATE label.projects SET version_id = @version_id WHERE id = @id AND workspace_id = @workspace_id;
