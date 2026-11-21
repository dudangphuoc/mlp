-- mlp:query InsertExport :exec
INSERT INTO registry.exports (id, workspace_id, model_version_id, format, state, job_id, created_by, created_at)
VALUES (@id, @workspace_id, @model_version_id, @format, 'running', @job_id, @created_by, @now);
