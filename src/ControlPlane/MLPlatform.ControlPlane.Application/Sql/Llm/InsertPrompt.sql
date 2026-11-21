-- mlp:query InsertPrompt :exec
INSERT INTO llmops.prompts (id, workspace_id, name, description, created_by, created_at) VALUES (@id, @workspace_id, @name, @description, @created_by, @now);
