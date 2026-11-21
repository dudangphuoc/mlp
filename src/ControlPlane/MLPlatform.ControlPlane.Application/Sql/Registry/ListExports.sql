-- mlp:query ListExports :many
SELECT id, workspace_id, model_version_id, format, state, error, job_id, file_name, size_bytes, created_at
FROM registry.exports WHERE workspace_id = @workspace_id AND model_version_id = @model_version_id ORDER BY created_at DESC LIMIT 50;
