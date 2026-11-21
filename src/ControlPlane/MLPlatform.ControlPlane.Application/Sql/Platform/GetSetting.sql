-- mlp:query GetSetting :one
SELECT key, enabled, config::text AS config_json, secrets_enc, updated_by, updated_at FROM platform.settings WHERE key = @key;
