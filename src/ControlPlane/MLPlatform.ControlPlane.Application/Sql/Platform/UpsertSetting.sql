-- mlp:query UpsertSetting :exec
INSERT INTO platform.settings (key, enabled, config, secrets_enc, updated_by, updated_at)
VALUES (@key, @enabled, @config_json::jsonb, @secrets_enc, @updated_by, @now)
ON CONFLICT (key) DO UPDATE SET enabled = excluded.enabled, config = excluded.config, secrets_enc = excluded.secrets_enc,
    updated_by = excluded.updated_by, updated_at = excluded.updated_at;
