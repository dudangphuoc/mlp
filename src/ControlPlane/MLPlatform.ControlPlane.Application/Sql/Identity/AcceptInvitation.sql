-- mlp:query AcceptInvitation :exec
UPDATE identity.invitations SET accepted_at = @now WHERE id = @id;
