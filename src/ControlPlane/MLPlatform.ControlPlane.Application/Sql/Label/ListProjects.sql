-- mlp:query ListProjects :many
SELECT id, workspace_id, dataset_id, name, guideline_md, schema_version, review_mode, lock_ttl_min, state, created_at, version_id, auto_accept_threshold, hermes_enabled
FROM label.projects WHERE workspace_id = @workspace_id AND state <> 'deleted' AND (@dataset_id::uuid IS NULL OR dataset_id = @dataset_id) ORDER BY created_at DESC;
