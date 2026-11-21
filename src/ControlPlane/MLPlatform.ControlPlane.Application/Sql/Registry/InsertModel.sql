-- mlp:query InsertModel :exec
INSERT INTO registry.models (id, workspace_id, name, task, description, created_by, created_at)
VALUES (@id, @workspace_id, @name, @task, @description, @created_by, @now);
