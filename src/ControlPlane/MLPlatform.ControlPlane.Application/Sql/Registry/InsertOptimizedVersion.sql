-- mlp:query InsertOptimizedVersion :exec
INSERT INTO registry.versions (id, model_id, version, run_id, recipe, variant, task, prefix, file_count, size_bytes, metrics, created_by, created_at,
    source, source_ref, parent_version_id, optimization)
VALUES (@id, @model_id, @version, @run_id, 'generic-onnx', 'onnx', @task, @prefix, @file_count, @size_bytes, '{}'::jsonb, @created_by, @now,
    'optimize', @source_ref, @parent_version_id, @optimization::jsonb);
