-- mlp:query InsertComment :exec
INSERT INTO label.comments (id, project_id, path, author_id, body, created_at) VALUES (@id, @project_id, @path, @author_id, @body, @now);
