-- mlp:query SetHermesSettings :exec
UPDATE label.projects SET hermes_enabled = @enabled, auto_accept_threshold = @threshold WHERE id = @id AND workspace_id = @workspace_id;
