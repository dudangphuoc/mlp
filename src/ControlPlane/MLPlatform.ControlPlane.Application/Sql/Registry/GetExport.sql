-- mlp:query GetExport :one
SELECT id, workspace_id, model_version_id, format, state, error, job_id, file_name, size_bytes, created_at
FROM registry.exports WHERE workspace_id = @workspace_id AND id = @id;
