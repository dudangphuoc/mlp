-- mlp:query UpdateConnection :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
UPDATE ai.connections SET name = @name, endpoint = @endpoint, secret_enc = @secret_enc, max_concurrency = @max_concurrency, rpm_limit = @rpm_limit,
    timeout_ms = @timeout_ms, is_active = @is_active, is_external = @is_external, updated_at = @now WHERE id = @id;
