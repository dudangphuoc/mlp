-- mlp:query GetEntity :one
SELECT id, name, join_key, description, created_at FROM feature.entities WHERE workspace_id = @workspace_id AND (id::text = @key OR name = @key);
