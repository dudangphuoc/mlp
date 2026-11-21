-- mlp:query DeleteConnection :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
DELETE FROM ai.connections WHERE id = @id;
