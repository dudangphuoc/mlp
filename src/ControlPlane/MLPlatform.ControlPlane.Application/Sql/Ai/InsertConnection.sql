-- mlp:query InsertConnection :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
INSERT INTO ai.connections (id, name, provider_type, endpoint, secret_enc, max_concurrency, rpm_limit, timeout_ms, is_active, is_external, created_by, created_at, updated_at)
VALUES (@id, @name, @provider_type, @endpoint, @secret_enc, @max_concurrency, @rpm_limit, @timeout_ms, true, @is_external, @created_by, @now, @now);
