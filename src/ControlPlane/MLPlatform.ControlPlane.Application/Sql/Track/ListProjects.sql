-- mlp:query ListProjects :many
SELECT id, workspace_id, slug, name, task, created_at FROM track.projects WHERE workspace_id = @workspace_id ORDER BY created_at DESC;
