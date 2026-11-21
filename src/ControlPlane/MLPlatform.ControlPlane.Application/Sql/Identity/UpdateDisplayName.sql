-- mlp:query UpdateDisplayName :exec
UPDATE identity.users SET display_name = @display_name WHERE id = @id;
