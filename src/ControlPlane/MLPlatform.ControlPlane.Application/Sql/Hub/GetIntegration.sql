-- mlp:query GetIntegration :one
SELECT workspace_id, kind, secret_enc, account, status, updated_at, last_checked_at FROM hub.integrations WHERE workspace_id = @workspace_id AND kind = @kind;
