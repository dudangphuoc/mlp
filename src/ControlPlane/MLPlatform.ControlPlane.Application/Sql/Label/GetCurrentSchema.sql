-- mlp:query GetCurrentSchema :one
SELECT id, dataset_id, version, task, classes::text AS classes, created_at FROM label.schemas WHERE dataset_id = @dataset_id AND is_current;
