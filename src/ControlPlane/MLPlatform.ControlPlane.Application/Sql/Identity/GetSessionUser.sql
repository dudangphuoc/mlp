-- mlp:query GetSessionUser :one
SELECT user_id, key_workspace_id AS workspace_id FROM identity.sessions WHERE token_hash = @token_hash AND revoked_at IS NULL;
