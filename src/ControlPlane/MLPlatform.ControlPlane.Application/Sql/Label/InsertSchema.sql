-- mlp:query InsertSchema :exec
UPDATE label.schemas SET is_current = false WHERE dataset_id = @dataset_id;
INSERT INTO label.schemas (id, dataset_id, version, task, classes, is_current, created_by, created_at)
VALUES (@id, @dataset_id,
        (SELECT coalesce(max(version), 0) + 1 FROM label.schemas WHERE dataset_id = @dataset_id),
        @task, @classes::jsonb, true, @created_by, @now);
