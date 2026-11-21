-- mlp:query GetSessionUser :scalar
SELECT user_id FROM identity.sessions WHERE token_hash = @token_hash AND revoked_at IS NULL;
