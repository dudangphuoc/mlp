-- mlp:query InsertApiKey :exec
INSERT INTO identity.sessions (id, user_id, token_hash, created_at, last_seen_at, kind, name)
VALUES (@id, @user_id, @token_hash, @now, @now, 'api_key', @name);
