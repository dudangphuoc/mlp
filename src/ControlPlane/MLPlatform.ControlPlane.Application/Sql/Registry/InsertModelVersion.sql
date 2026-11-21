-- mlp:query InsertModelVersion :exec
INSERT INTO registry.versions (id, model_id, version, run_id, recipe, variant, task, prefix, file_count, size_bytes, metrics, created_by, created_at)
VALUES (@id, @model_id, @version, @run_id, @recipe, @variant, @task, @prefix, @file_count, @size_bytes, @metrics::jsonb, @created_by, @now);
