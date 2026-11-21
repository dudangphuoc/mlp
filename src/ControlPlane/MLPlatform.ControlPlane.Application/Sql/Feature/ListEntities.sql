-- mlp:query ListEntities :many
SELECT id, name, join_key, description, created_at FROM feature.entities WHERE workspace_id = @workspace_id ORDER BY name;
