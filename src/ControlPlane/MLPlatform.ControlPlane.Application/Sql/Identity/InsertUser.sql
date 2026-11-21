-- mlp:query InsertUser :exec
INSERT INTO identity.users (id, email, username, display_name, password_hash, is_instance_admin, created_at)
VALUES (@id, @email, @username, @display_name, @password_hash, @is_instance_admin, @created_at);
