-- mlp:query ListApiKeys :many
SELECT id, name, created_at, last_seen_at FROM identity.sessions
WHERE user_id = @user_id AND kind = 'api_key' AND revoked_at IS NULL ORDER BY created_at DESC;
