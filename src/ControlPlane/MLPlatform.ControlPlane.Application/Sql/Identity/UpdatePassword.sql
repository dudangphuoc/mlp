-- mlp:query UpdatePassword :exec
UPDATE identity.users SET password_hash = @password_hash WHERE id = @id;
