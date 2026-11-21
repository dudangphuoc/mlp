-- mlp:query RevokeApiKey :scalar
WITH r AS (UPDATE identity.sessions SET revoked_at = @now WHERE id = @id AND user_id = @user_id AND kind = 'api_key' AND revoked_at IS NULL RETURNING 1)
SELECT count(*) FROM r;
