-- mlp:query RevokeSession :exec
UPDATE identity.sessions SET revoked_at = @now WHERE token_hash = @token_hash AND revoked_at IS NULL;
