-- mlp:query InsertDataset :exec
INSERT INTO data.datasets (id, workspace_id, slug, name, task, owner_id, status, created_at)
VALUES (@id, @workspace_id, @slug, @name, @task, @owner_id, 'ready', @now);
