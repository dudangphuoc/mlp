-- mlp:query DeleteIntegration :exec
DELETE FROM hub.integrations WHERE workspace_id = @workspace_id AND kind = @kind;
