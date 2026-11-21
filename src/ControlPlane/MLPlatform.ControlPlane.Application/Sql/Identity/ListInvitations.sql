-- mlp:query ListInvitations :many
SELECT id, email, role, created_at, expires_at, accepted_at FROM identity.invitations
WHERE workspace_id = @workspace_id AND revoked_at IS NULL ORDER BY created_at DESC LIMIT 200;
