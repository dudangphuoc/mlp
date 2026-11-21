-- mlp:query UserNames :many
SELECT id, display_name FROM identity.users WHERE id = ANY(@ids);
