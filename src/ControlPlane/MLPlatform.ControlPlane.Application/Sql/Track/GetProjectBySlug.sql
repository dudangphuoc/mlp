-- mlp:query GetProjectBySlug :one
SELECT id, workspace_id, slug, name, task, created_at FROM track.projects WHERE workspace_id = @workspace_id AND slug = @slug;
