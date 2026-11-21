-- mlp:query GetConnection :one
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
SELECT id, name, provider_type, endpoint, secret_enc, max_concurrency, rpm_limit, timeout_ms, is_active, created_at, updated_at, is_external FROM ai.connections WHERE id = @id;
