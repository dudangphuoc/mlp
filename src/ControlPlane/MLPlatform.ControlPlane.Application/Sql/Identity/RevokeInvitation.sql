-- mlp:query RevokeInvitation :exec
UPDATE identity.invitations SET revoked_at = @now WHERE id = @id AND workspace_id = @workspace_id AND accepted_at IS NULL;
