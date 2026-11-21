-- mlp:query RevokeOtherSessions :exec
UPDATE identity.sessions SET revoked_at = @now WHERE user_id = @user_id AND kind = 'session' AND revoked_at IS NULL AND token_hash <> @keep_hash;
