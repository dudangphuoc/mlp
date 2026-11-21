-- mlp:query GetProject :one
SELECT id, workspace_id, slug, name, task, created_at FROM track.projects WHERE id = @id AND workspace_id = @workspace_id;
