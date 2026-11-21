-- mlp:query GetEnrollmentByToken :one
SELECT id, workspace_id, data_path, models_path, expires_at, used_at
FROM fleet.enrollments WHERE token_hash = @token_hash FOR UPDATE;
