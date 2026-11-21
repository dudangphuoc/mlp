-- mlp:query GetModelByName :one
SELECT id, workspace_id, name, task, created_at FROM registry.models WHERE workspace_id = @workspace_id AND name = @name;
