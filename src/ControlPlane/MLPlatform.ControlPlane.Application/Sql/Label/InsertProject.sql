-- mlp:query InsertProject :exec
INSERT INTO label.projects (id, workspace_id, dataset_id, name, guideline_md, schema_version, review_mode, lock_ttl_min, state, created_by, created_at, version_id, auto_accept_threshold)
VALUES (@id, @workspace_id, @dataset_id, @name, @guideline_md, @schema_version, @review_mode, @lock_ttl_min, 'draft', @created_by, @now, @version_id, @auto_accept_threshold);
