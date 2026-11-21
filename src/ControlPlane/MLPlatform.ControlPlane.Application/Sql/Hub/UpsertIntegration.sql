-- mlp:query UpsertIntegration :exec
INSERT INTO hub.integrations (workspace_id, kind, secret_enc, account, status, created_by, updated_at, last_checked_at)
VALUES (@workspace_id, @kind, @secret_enc, @account, @status, @created_by, @now, @now)
ON CONFLICT (workspace_id, kind) DO UPDATE SET secret_enc = excluded.secret_enc, account = excluded.account, status = excluded.status,
    created_by = excluded.created_by, updated_at = excluded.updated_at, last_checked_at = excluded.last_checked_at;
