-- mlp:query InsertEvaluation :exec
INSERT INTO registry.evaluations (id, workspace_id, model_version_id, dataset_version_id, path_filter, max_samples, conf_threshold, state, created_by, created_at)
VALUES (@id, @workspace_id, @model_version_id, @dataset_version_id, @path_filter, @max_samples, @conf_threshold, 'queued', @created_by, @now);
