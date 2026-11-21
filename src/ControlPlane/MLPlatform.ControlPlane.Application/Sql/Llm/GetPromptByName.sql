-- mlp:query GetPromptByName :one
SELECT id, name, description, created_at, 0 AS latest_version FROM llmops.prompts WHERE workspace_id = @workspace_id AND (id::text = @key OR name = @key);
