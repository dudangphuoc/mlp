-- mlp:query DeleteModel :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
DELETE FROM ai.models WHERE id = @id;
