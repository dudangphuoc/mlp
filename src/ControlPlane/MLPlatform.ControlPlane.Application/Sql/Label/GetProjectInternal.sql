-- mlp:query GetProjectInternal :one
-- mlp:no-workspace background runner; the project id comes from a workspace-scoped job row
SELECT id, workspace_id, dataset_id, name, guideline_md, schema_version, review_mode, lock_ttl_min, state, created_at, version_id, auto_accept_threshold, hermes_enabled
FROM label.projects WHERE id = @id;
