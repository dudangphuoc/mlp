-- mlp:query InsertEnrollment :exec
INSERT INTO fleet.enrollments (id, workspace_id, token_hash, os, data_path, models_path, created_by, created_at, expires_at)
VALUES (@id, @workspace_id, @token_hash, @os, @data_path, @models_path, @created_by, @created_at, @expires_at);
