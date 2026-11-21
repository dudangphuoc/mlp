-- mlp:query InsertInvitation :exec
INSERT INTO identity.invitations (id, workspace_id, email, role, token_hash, created_by, created_at, expires_at)
VALUES (@id, @workspace_id, @email, @role, @token_hash, @created_by, @now, @expires_at);
