-- mlp:query InsertEntity :exec
INSERT INTO feature.entities (id, workspace_id, name, join_key, description, created_by, created_at) VALUES (@id, @workspace_id, @name, @join_key, @description, @created_by, @now);
