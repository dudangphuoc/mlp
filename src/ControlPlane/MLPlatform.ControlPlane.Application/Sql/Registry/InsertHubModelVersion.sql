-- mlp:query InsertHubModelVersion :exec
INSERT INTO registry.versions (id, model_id, version, run_id, recipe, variant, task, prefix, file_count, size_bytes, metrics, created_by, created_at, source, source_ref)
VALUES (@id, @model_id, @version, NULL, 'hf-auto', 'hub-model', @task, @prefix, 0, 0, '{}'::jsonb, @created_by, @now, @source, @source_ref);
