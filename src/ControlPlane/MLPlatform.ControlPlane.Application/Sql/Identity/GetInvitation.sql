-- mlp:query GetInvitation :one
SELECT id, workspace_id, email, role, expires_at, accepted_at FROM identity.invitations WHERE token_hash = @token_hash AND revoked_at IS NULL FOR UPDATE;
