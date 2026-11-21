-- mlp:query InsertProject :exec
INSERT INTO track.projects (id, workspace_id, slug, name, task, created_by, created_at)
VALUES (@id, @workspace_id, @slug, @name, @task, @created_by, @now);
