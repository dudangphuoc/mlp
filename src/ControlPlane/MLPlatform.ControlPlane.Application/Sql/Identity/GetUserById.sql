-- mlp:query GetUserById :one
SELECT id, email, username, display_name, password_hash, status, is_instance_admin
FROM identity.users WHERE id = @id;
