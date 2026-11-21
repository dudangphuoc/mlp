-- mlp:query InsertSession :exec
INSERT INTO identity.sessions (id, user_id, token_hash, created_at, last_seen_at)
VALUES (@id, @user_id, @token_hash, @created_at, @created_at);
