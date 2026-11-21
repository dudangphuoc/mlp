-- mlp:query GetProject :one
SELECT id, workspace_id, dataset_id, name, guideline_md, schema_version, review_mode, lock_ttl_min, state, created_at, version_id, auto_accept_threshold, hermes_enabled
FROM label.projects WHERE id = @id AND workspace_id = @workspace_id AND state <> 'deleted';
