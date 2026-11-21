-- mlp:query ListSettings :many
SELECT key, enabled, config::text AS config_json, secrets_enc, updated_by, updated_at FROM platform.settings ORDER BY key;
